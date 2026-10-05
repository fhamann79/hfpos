using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.Tests.Unit;
using Xunit;

namespace Hfpos.FiscalSmokeHost;

// A release rehearsal executable, never a production API dependency or publish output.
internal static class ReleasePilotSmoke
{
    internal static string Step { get; private set; } = "configuration";
    private const string TenantPassword = "Synthetic-only-531-tenant";
    private const string ProtectedValue = "Synthetic-only-531-protected-value";
    private const string ApplicationName = "HFPOS-SMOKE-ONLY";
    private static string PlatformPassword => Environment.GetEnvironmentVariable("SMOKE_PLATFORM_PASSWORD") ?? "Synthetic-only-531-platform";

    internal static async Task RunAsync(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HF_POS_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Synthetic connection required.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        var compose = Environment.GetEnvironmentVariable("HF_POS_RELEASE_COMPOSE") == "1";
        var restore = args.Contains("--verify-restored");
        var expected = compose ? (restore ? "hfpos_ops_restore" : "hfpos_ops_ci") : (restore ? "hfpos_test_531_restore" : "hfpos_test_531");
        if (parsed.Database != expected || parsed.Username != (compose ? "hfpos_smoke" : "hfpos_test")
            || (compose ? parsed.Host != "postgres" : parsed.Host is not ("127.0.0.1" or "localhost")))
            throw new InvalidOperationException("Release rehearsal accepts only its dedicated synthetic source/restore database.");
        var keys = Path.GetFullPath(Environment.GetEnvironmentVariable("HF_POS_RELEASE_KEYS") ?? throw new InvalidOperationException("Synthetic keyring required."));
        if (!Directory.Exists(keys)) throw new InvalidOperationException("Synthetic keyring mount missing.");
        if (args.Contains("--initialize") && !compose && !restore)
            await new PostgresDatabaseFixture().RecreateDatabaseAsync();
        if (args.Contains("--wrong-key"))
        {
            Assert.Empty(Directory.GetFiles(keys));
            DataProtectionProvider.Create(new DirectoryInfo(keys), b => b.SetApplicationName(ApplicationName))
                .CreateProtector(CompanyEmailProtectionPurposes.SmtpPasswordV1).Protect("Unrelated synthetic keyring");
        }
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("ASPNETCORE_PREVENTHOSTINGSTARTUP", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_POS_BACKEND_API", AppContext.BaseDirectory);
        // Program registers the DP repository before WebApplicationFactory applies late configuration.
        Environment.SetEnvironmentVariable("DataProtection__KeysPath", keys);
        Environment.SetEnvironmentVariable("DataProtection__ApplicationName", args.Contains("--wrong-application") ? "SYNTHETIC-WRONG-APPLICATION" : ApplicationName);
        Step = "app-startup";
        await using var app = new PilotFactory(connection, keys, args.Contains("--wrong-application"));
        using var platform = app.CreateClient(new() { BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false });
        using var tenant = app.CreateClient(new() { BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false });
        using (var scope = app.Services.CreateScope())
        {
            var repository = Assert.IsType<FileSystemXmlRepository>(scope.ServiceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
            Assert.Equal(keys, repository.Directory.FullName);
            Assert.Equal(args.Contains("--wrong-application") ? "SYNTHETIC-WRONG-APPLICATION" : ApplicationName,
                scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
            Assert.Equal(expected, await scope.ServiceProvider.GetRequiredService<PosDbContext>().Database
                .SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync());
        }
        if (args.Contains("--initialize"))
        {
            if (restore) throw new InvalidOperationException("Never initialize a restored database.");
            await InitializeAsync(app, platform, tenant, connection);
        }
        else
        {
            if (!restore) throw new InvalidOperationException("Verification must target the restored database.");
            await LoginAsync(platform, "/api/platform/auth/login", "smoke-platform", PlatformPassword);
            await LoginAsync(tenant, "/api/Auth/login", "pilot-tenant", TenantPassword);
            await AssertBusinessAsync(app, platform, tenant, keys, args.Contains("--expect-key-failure"));
            Console.WriteLine(args.Contains("--expect-key-failure") ? "RESTORED APP WRONG/MISSING KEY FAIL-CLOSED PASS" : "RESTORED APP LOGIN/PROTECTED VALUE/BUSINESS PASS");
        }
        Assert.DoesNotContain(typeof(PosDbContext).Assembly.GetReferencedAssemblies(), a => a.Name is "Hfpos.FiscalSmokeHost" or "Pos.Backend.Api.Tests");
    }

    private static async Task InitializeAsync(PilotFactory app, HttpClient platform, HttpClient tenant, string connection)
    {
        using (var scope = app.Services.CreateScope())
            await PlatformBootstrap.RunAsync(scope.ServiceProvider.GetRequiredService<PosDbContext>(), new()
            { Enabled = true, Username = "smoke-platform", Email = "smoke-platform@example.invalid", Password = PlatformPassword });
        await LoginAsync(platform, "/api/platform/auth/login", "smoke-platform", PlatformPassword);
        var provision = await PostAsync<TenantProvisionResult>(platform, "/api/platform/tenants", new TenantProvisionRequest(Guid.NewGuid(),
            new("SYNTHETIC HF One PILOT", "9000000000531", "America/Guayaquil"), new("Pilot source", "Synthetic avenue"),
            new("Pilot point"), new("pilot-tenant", "pilot-tenant@example.invalid", TenantPassword)));
        await LoginAsync(tenant, "/api/Auth/login", "pilot-tenant", TenantPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tenant.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await platform.GetAsync("/api/Auth/me")).StatusCode);
        var product = 0; var supplier = 0;
        foreach (var (kind, csv) in new[]
        {
            ("categories", "name\nPilot category\n"),
            ("products", "internalCode,barcode,name,category,price,cost,minimumStock,vatCategory\nPILOT-531,,Synthetic pilot product,Pilot category,10,3,1,Vat0\n"),
            ("customers", "name,identificationType,identification,phone,email,address,notes\nSynthetic buyer,06,PILOT531,,buyer@example.invalid,Synthetic address,\n"),
            ("suppliers", "name,identification,phone,email,address,notes\nSynthetic supplier,PILOT531,,supplier@example.invalid,Synthetic address,\n"),
            ("opening-inventory", "internalCode,quantity\nPILOT-531,20\n")
        })
        {
            var payload = new InitialDataPreviewRequest { Kind = kind, Csv = csv, RequestId = Guid.NewGuid() };
            var preview = await PostAsync<InitialDataPreviewDto>(tenant, "/api/initial-data/preview", payload);
            Assert.True(preview.CanConfirm);
            var result = await PostAsync<InitialDataResultDto>(tenant, "/api/initial-data/confirm", new { payload, previewToken = preview.PreviewToken });
            if (kind == "products") product = Assert.Single(result.CreatedIds);
            if (kind == "suppliers") supplier = Assert.Single(result.CreatedIds);
        }
        await LoginAsync(tenant, "/api/Auth/login", "pilot-tenant", TenantPassword);
        var cash = await PostAsync<CashSessionDto>(tenant, "/api/CashSessions/open", new OpenCashSessionDto { OpeningAmount = 10 });
        var request = new SaleCreateDto { RequestId = Guid.NewGuid(), CashReceived = 20, DocumentType = SaleDocumentType.Ticket,
            Items = [new() { ProductId = product, Quantity = 1, UnitPrice = 10 }] };
        var ticket = await PostAsync<SaleDto>(tenant, "/api/Sales", request);
        Assert.Equal(10, ticket.Total); Assert.Equal(10, ticket.CashChange);
        Assert.Equal(ticket.Id, (await PostAsync<SaleDto>(tenant, "/api/Sales", request)).Id);
        Assert.Equal(ticket.CashChange, (await GetAsync<SaleDto>(tenant, $"/api/Sales/{ticket.Id}")).CashChange);
        Console.WriteLine("PILOT PLATFORM/PROVISION/IMPORT/LOGIN/CASH/TICKET/CHANGE/REPLAY PASS; browser print is a separate human gate");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            var context = await TenantContextAsync(db);
            using var rsa = RSA.Create(2048);
            var certRequest = new CertificateRequest("CN=HF One synthetic pilot only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            using var bytes = new MemoryStream(certificate.Export(X509ContentType.Pfx, "synthetic-only"));
            var file = new FormFile(bytes, 0, bytes.Length, "certificate", "synthetic.pfx") { Headers = new HeaderDictionary(), ContentType = "application/x-pkcs12" };
            await new SriCertificateService(db, new StaticOperationalContextAccessor(context), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
                scope.ServiceProvider.GetRequiredService<ILogger<SriCertificateService>>()).UploadCertificateAsync(file, "synthetic-only");
        }
        await SmokeTransport.InitializeAsync(connection);
        using (var response = await tenant.PutAsJsonAsync("/api/FiscalSettings/sri", new { environment = 1, emissionType = 1, isEnabled = true, automaticProcessingEnabled = true }))
            response.EnsureSuccessStatusCode();
        request.RequestId = Guid.NewGuid(); request.DocumentType = SaleDocumentType.Invoice; request.PaymentMethod = SalePaymentMethod.Card; request.CashReceived = null;
        var invoice = await PostAsync<SaleDto>(tenant, "/api/Sales", request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        do
        {
            invoice = await GetAsync<SaleDto>(tenant, $"/api/Sales/{invoice.Id}");
            if (invoice.DocumentStatus == SaleDocumentStatus.Authorized) break;
            await Task.Delay(200, timeout.Token);
        } while (!timeout.IsCancellationRequested);
        Assert.Equal(SaleDocumentStatus.Authorized, invoice.DocumentStatus);
        using (var ride = await tenant.GetAsync($"/api/Sales/{invoice.Id}/sri/ride-pdf"))
        { ride.EnsureSuccessStatusCode(); Assert.Equal("application/pdf", ride.Content.Headers.ContentType?.MediaType); Assert.NotEmpty(await ride.Content.ReadAsByteArrayAsync()); }
        await PostAsync<PurchaseReceiptDto>(tenant, "/api/PurchaseReceipts", new PurchaseReceiptCreateDto { RequestId = Guid.NewGuid(), SupplierId = supplier,
            ReceiptDate = DateTime.UtcNow, SupplierDocumentNumber = "SYNTHETIC-531", Items = [new() { ProductId = product, Quantity = 5, UnitCost = 4 }] });
        var destination = await PostAsync<EstablishmentDto>(tenant, "/api/Establishments", new { name = "Pilot destination", code = "002", address = "Synthetic destination" });
        var transfer = await PostAsync<InventoryTransferDetailDto>(tenant, "/api/Inventory/transfers", new InventoryTransferCreateDto { RequestId = Guid.NewGuid(),
            DestinationEstablishmentId = destination.Id, Items = [new() { ProductId = product, Quantity = 2 }] });
        Assert.Equal(21, Assert.Single(transfer.Items).SourceStockAfter); Assert.Equal(2, transfer.Items[0].DestinationStockAfter);
        var note = await PostAsync<CreditNoteDto>(tenant, "/api/CreditNotes/drafts", new CreateCreditNoteDraftDto { OriginalSaleId = invoice.Id, Reason = "Synthetic pilot return",
            Items = [new() { SaleItemId = invoice.Items[0].Id, Quantity = 1 }] });
        foreach (var action in new[] { "prepare-draft", "sign", "submit", "check-authorization" })
        { using var response = await tenant.PostAsync($"/api/CreditNotes/{note.Id}/sri/{action}", null); response.EnsureSuccessStatusCode(); }
        note = await GetAsync<CreditNoteDto>(tenant, $"/api/CreditNotes/{note.Id}");
        Assert.Equal(SaleDocumentStatus.Authorized, note.DocumentStatus);
        using (var returned = await tenant.PostAsJsonAsync($"/api/CreditNotes/{note.Id}/inventory-return", new { notes = "Synthetic return" })) returned.EnsureSuccessStatusCode();
        await PostAsync<CreditNoteRefundDto>(tenant, $"/api/CreditNotes/{note.Id}/refund", new RefundCreditNoteDto { Method = SalePaymentMethod.Card });
        var closed = await PostAsync<CashSessionDto>(tenant, $"/api/CashSessions/{cash.Id}/close", new CloseCashSessionDto { CountedCashAmount = 20 });
        Assert.Equal(20, closed.ExpectedCashAmount); Assert.Equal(0, closed.DifferenceAmount);
        app.Clock.Now = app.Clock.Now.AddDays(1);
        var settlement = await PostAsync<PaymentSettlementDto>(tenant, "/api/PaymentSettlements", new PaymentSettlementCreateDto { RequestId = Guid.NewGuid(),
            BusinessDate = invoice.BusinessDate, PaymentMethod = SalePaymentMethod.Card, SettledAmount = 0 });
        Assert.Equal(10, settlement.GrossSalesAmount); Assert.Equal(10, settlement.RefundAmount); Assert.Equal(0, settlement.ExpectedNetAmount);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
            var context = await TenantContextAsync(db);
            var email = new CompanyEmailSettingsService(db, new StaticOperationalContextAccessor(context), new NoEmail(),
                scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), scope.ServiceProvider.GetRequiredService<ILogger<CompanyEmailSettingsService>>());
            await email.UpdateAsync(new() { IsEnabled = true, SmtpHost = "smtp.synthetic.invalid", SmtpPort = 587, EncryptionMode = "StartTls",
                SmtpUsername = "synthetic-only", SmtpPassword = ProtectedValue, FromEmail = "pilot@example.invalid" });
            Assert.True((await email.SendTestAsync(new() { ToEmail = "proof@example.invalid" })).Success);
            Assert.Equal(ProtectedValue, (await email.GetConfiguredSenderSettingsAsync()).SmtpPassword);
            var settings = await db.CompanyEmailSettings.SingleAsync(e => e.CompanyId == provision.Tenant.Company.Id);
            Assert.NotEqual(ProtectedValue, settings.SmtpPasswordProtected);
        }
        await AssertBusinessAsync(app, platform, tenant, app.Keys, false);
        Console.WriteLine("PILOT INVOICE/BACKGROUND FAKE/PDF/PURCHASE/STOCK/TRANSFER/NC/REFUND/CLOSE/SETTLEMENT/PROTECTED VALUE PASS");
    }

    private static async Task AssertBusinessAsync(PilotFactory app, HttpClient platform, HttpClient tenant, string keys, bool failKey)
    {
        Step = "restored-api-business";
        (await platform.GetAsync("/api/platform/tenants")).EnsureSuccessStatusCode();
        (await tenant.GetAsync("/api/Auth/me")).EnsureSuccessStatusCode();
        (await tenant.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope();
        Step = "restored-data-business";
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var context = await TenantContextAsync(db);
        var sales = await db.Sales.AsNoTracking().Include(s => s.Items).Where(s => s.CompanyId == context.CompanyId).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, sales.Count); Assert.Equal(20, sales.Sum(s => s.Total));
        Assert.Equal(SaleDocumentStatus.Authorized, sales[1].DocumentStatus);
        Assert.Equal(ElectronicIssuingJobState.Authorized, (await db.ElectronicIssuingJobs.SingleAsync(j => j.SaleId == sales[1].Id)).State);
        Assert.All(await db.ElectronicIssuingJobs.AsNoTracking().ToListAsync(), job => Assert.Equal(ElectronicIssuingJobState.Authorized, job.State));
        Assert.Equal(1, await db.PurchaseReceipts.CountAsync(p => p.CompanyId == context.CompanyId));
        Assert.Equal(1, await db.InventoryTransfers.CountAsync(t => t.CompanyId == context.CompanyId));
        Assert.Equal(1, await db.CreditNoteRefunds.CountAsync(r => r.CompanyId == context.CompanyId));
        Assert.All(sales.SelectMany(s => s.Items), item =>
        {
            Assert.Equal(3, item.UnitCost); Assert.Equal(3, item.LineCost); Assert.Equal(7, item.GrossProfit);
            Assert.Equal("Synthetic pilot product", item.ProductNameSnapshot);
        });
        Assert.Equal(4, (await db.Products.SingleAsync(p => p.CompanyId == context.CompanyId)).Cost);
        var purchase = await db.PurchaseReceipts.Include(p => p.Items).SingleAsync(p => p.CompanyId == context.CompanyId);
        Assert.Equal(4, Assert.Single(purchase.Items).UnitCost);
        var note = await db.CreditNotes.Include(n => n.Items).SingleAsync(n => n.CompanyId == context.CompanyId);
        Assert.Equal(SaleDocumentStatus.Authorized, note.DocumentStatus); Assert.Equal(10, note.Total);
        Assert.Equal(10, (await db.CreditNoteRefunds.SingleAsync(r => r.CompanyId == context.CompanyId)).Amount);
        Assert.Equal(sales[1].Number, note.OriginalSaleNumberSnapshot);
        Assert.Equal(sales[1].AccessKey, note.OriginalSaleAccessKeySnapshot);
        Assert.Equal(sales[1].AuthorizationNumber, note.OriginalSaleAuthorizationNumberSnapshot);
        Assert.Equal(3, Assert.Single(note.Items).UnitCost); Assert.Equal(3, note.Items.Single().LineCost);
        Assert.False(string.IsNullOrWhiteSpace(sales[1].SriSignedXml));
        Assert.False(string.IsNullOrWhiteSpace(note.SriSignedXml));
        Assert.False(string.IsNullOrWhiteSpace(sales[1].AuthorizationNumber));
        Assert.False(string.IsNullOrWhiteSpace(note.AuthorizationNumber));
        Assert.Equal(0, (await db.PaymentSettlements.SingleAsync(s => s.CompanyId == context.CompanyId)).ExpectedNetAmount);
        var stocks = await db.ProductStocks.Where(s => s.CompanyId == context.CompanyId).OrderBy(s => s.EstablishmentId).ToListAsync();
        Assert.Equal(new decimal[] { 22, 2 }, stocks.Select(s => s.Quantity));
        var closed = await db.CashSessions.SingleAsync(s => s.CompanyId == context.CompanyId);
        Assert.Equal(CashSessionStatus.Closed, closed.Status); Assert.Equal(20, closed.ExpectedCashAmount); Assert.Equal(0, closed.DifferenceAmount);
        var email = new CompanyEmailSettingsService(db, new StaticOperationalContextAccessor(context), new NoEmail(),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), scope.ServiceProvider.GetRequiredService<ILogger<CompanyEmailSettingsService>>());
        var certificateProvider = new SriSigningCertificateProvider(db, new StaticOperationalContextAccessor(context),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), scope.ServiceProvider.GetRequiredService<ILogger<SriSigningCertificateProvider>>());
        if (failKey)
        {
            Step = "restored-negative-unprotect";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => email.GetConfiguredSenderSettingsAsync());
            Assert.Equal("COMPANY_EMAIL_OPERATION_FAILED", error.Message);
            var certificateError = await Assert.ThrowsAsync<InvalidOperationException>(() => certificateProvider.GetActiveCertificateMaterialAsync());
            Assert.Equal("CERTIFICATE_UNPROTECT_FAILED", certificateError.Message);
        }
        else
        {
            Step = "restored-positive-unprotect";
            Assert.Equal(ProtectedValue, (await email.GetConfiguredSenderSettingsAsync()).SmtpPassword);
            using var material = await certificateProvider.GetActiveCertificateMaterialAsync();
            Assert.True(material.Certificate.HasPrivateKey);
            Assert.Equal("CN=HF One synthetic pilot only", material.Subject);
            Assert.True(material.NotAfter > DateTime.UtcNow);
            Assert.True(material.Certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow);
            var stored = await db.CompanyEmailSettings.SingleAsync(e => e.CompanyId == context.CompanyId);
            var wrongPurpose = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("SYNTHETIC-WRONG-PURPOSE");
            Assert.Throws<CryptographicException>(() => wrongPurpose.Unprotect(stored.SmtpPasswordProtected!));
        }
        var actual = scope.ServiceProvider.GetRequiredService<IConfiguration>()["DataProtection:KeysPath"];
        Assert.Equal(keys, actual);
        Assert.Equal(10m, sales[0].CashChange);
    }

    private static async Task<OperationalContext> TenantContextAsync(PosDbContext db)
    {
        var user = await db.Users.Include(u => u.Role).Include(u => u.Company).SingleAsync(u => u.Username == "pilot-tenant");
        return new() { CompanyId = user.CompanyId, EstablishmentId = user.EstablishmentId ?? throw new InvalidOperationException("Synthetic establishment missing."), EmissionPointId = user.EmissionPointId,
            UserId = user.Id, Username = user.Username, CompanyTimeZoneId = user.Company.TimeZoneId,
            UserSessionVersion = user.SessionVersion, RoleAuthorizationVersion = user.Role.AuthorizationVersion };
    }
    private static async Task LoginAsync(HttpClient client, string path, string username, string password)
    {
        var login = await PostAsync<JsonElement>(client, path, new { username, password });
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("token").GetString());
    }
    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        Step = path;
        using var response = await client.PostAsJsonAsync(path, body);
        if (!response.IsSuccessStatusCode)
        {
            Step = $"{path}/http-{(int)response.StatusCode}";
            throw new InvalidOperationException("Synthetic HTTP step failed.");
        }
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }

    private sealed class PilotFactory(string connection, string keys, bool wrongApplication = false) : WebApplicationFactory<Program>
    {
        internal string Keys => keys;
        internal PilotClock Clock { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production").UseContentRoot(AppContext.BaseDirectory);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = ProductionConfigurationTests.Valid(keys);
                settings["ConnectionStrings:DefaultConnection"] = connection;
                settings["DataProtection:ApplicationName"] = wrongApplication ? "SYNTHETIC-WRONG-APPLICATION" : ApplicationName;
                settings["Sri:ReceptionTestUrl"] = "https://synthetic-sri.invalid/never-called";
                settings["Sri:AuthorizationTestUrl"] = "https://synthetic-sri.invalid/never-called";
                settings["Observability:Enabled"] = "false";
                configuration.Sources.Clear(); configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<SmokeTransport>();
                services.AddSingleton<IHttpMessageHandlerBuilderFilter, SyntheticHttpFilter>();
                services.Replace(ServiceDescriptor.Singleton<IBusinessClockService>(Clock));
                services.Replace(ServiceDescriptor.Scoped<IEmailSenderService>(_ => new NoEmail()));
            });
        }
    }
    private sealed class PilotClock : IBusinessClockService
    {
        private readonly BusinessClockService inner = new();
        internal DateTime Now = new(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public TimeZoneInfo ResolveTimeZone(string id) => inner.ResolveTimeZone(id);
        public DateOnly GetBusinessDate(DateTime time, string id) => inner.GetBusinessDate(time, id);
        public DateTime GetBusinessDateStartUtc(DateOnly day, string id) => inner.GetBusinessDateStartUtc(day, id);
        public DateTime GetBusinessDateEndExclusiveUtc(DateOnly day, string id) => inner.GetBusinessDateEndExclusiveUtc(day, id);
        public BusinessDateRange GetBusinessDateRangeUtc(DateOnly from, DateOnly to, string id) => inner.GetBusinessDateRangeUtc(from, to, id);
    }
    private sealed class NoEmail : IEmailSenderService
    {
        public Task SendAsync(Pos.Backend.Api.Core.Entities.CompanyEmailSettings settings, string password, OutboundEmailMessage message, CancellationToken cancellationToken = default)
        {
            Assert.Equal("smtp.synthetic.invalid", settings.SmtpHost);
            Assert.Equal(ProtectedValue, password);
            Assert.Equal("proof@example.invalid", message.To);
            return Task.CompletedTask; // No SMTP connection, transport or credential ever leaves the process.
        }
    }
}
