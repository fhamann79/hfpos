using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed class AutonomousIssuingFixture(PostgresDatabaseFixture database, IDataProtectionProvider? protection = null)
{
    public TestTenant Tenant { get; private set; } = null!;
    public OperationalContext Admin { get; private set; } = null!;
    public IDataProtectionProvider Protection { get; } = protection ?? new EphemeralDataProtectionProvider();
    public SyntheticSriTransport Transport { get; } = new();
    public IOptions<SriOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new SriOptions
    { Environment = 1, EmissionType = 1, AllowProductionSubmission = false });

    public async Task InitializeAsync(string key, int seed)
    {
        Tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, 50m);
        Tenant.OperationalContext.UserSessionVersion = 1; Tenant.OperationalContext.RoleAuthorizationVersion = 1;
        await using (var db = database.CreateDbContext())
        {
            (await db.Companies.SingleAsync(c => c.Id == Tenant.CompanyId)).MatrixAddress = "Synthetic matrix address";
            var cashier = await db.Users.SingleAsync(u => u.Id == Tenant.UserId);
            var codes = new[] { AppPermissions.PosSalesCreate, AppPermissions.FiscalSettingsWrite,
                AppPermissions.FiscalSettingsRead, AppPermissions.SriDocumentsSign, AppPermissions.SriDocumentsSubmit, AppPermissions.ReportsSalesRead };
            foreach (var code in codes)
                if (!await db.Permissions.AnyAsync(p => p.Code == code))
                    db.Permissions.Add(new Permission { Code = code, Description = code, IsActive = true, CreatedAt = DateTime.UtcNow });
            var role = new Role { CompanyId = Tenant.CompanyId, Code = AppRoles.Admin, Name = "Synthetic fiscal admin", IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            foreach (var permission in await db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync())
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                if (permission.Code == AppPermissions.PosSalesCreate)
                    db.RolePermissions.Add(new RolePermission { RoleId = cashier.RoleId, PermissionId = permission.Id });
            }
            var admin = new User { CompanyId = Tenant.CompanyId, EstablishmentId = Tenant.EstablishmentId,
                EmissionPointId = Tenant.EmissionPointId, RoleId = role.Id, Username = $"fiscal-{key}", Email = $"fiscal-{key}@hfpos.test",
                PasswordHash = "synthetic-only", IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Users.Add(admin); await db.SaveChangesAsync();
            Admin = new OperationalContext { CompanyId = Tenant.CompanyId, EstablishmentId = Tenant.EstablishmentId,
                EmissionPointId = Tenant.EmissionPointId, UserId = admin.Id, Username = admin.Username,
                CompanyTimeZoneId = "America/Guayaquil", UserSessionVersion = 1, RoleAuthorizationVersion = 1 };
        }
        await using (var scope = new TestServiceScope(database, Tenant.OperationalContext))
            await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0m });
        await using (var db = database.CreateDbContext())
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=HFPOS synthetic fiscal test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            using var stream = new MemoryStream(certificate.Export(X509ContentType.Pfx, "synthetic-only"));
            var file = new FormFile(stream, 0, stream.Length, "certificate", "synthetic.pfx") { ContentType = "application/x-pkcs12" };
            await new SriCertificateService(db, new StaticOperationalContextAccessor(Admin), Protection,
                NullLogger<SriCertificateService>.Instance).UploadCertificateAsync(file, "synthetic-only");
        }
        await SetDelegationAsync(true);
    }

    public Task<CompanySriSettingsDto> SetDelegationAsync(bool enabled, PosDbContext? db = null)
        => WithSettingsAsync(enabled, db);

    private async Task<CompanySriSettingsDto> WithSettingsAsync(bool enabled, PosDbContext? supplied)
    {
        await using var owned = supplied is null ? database.CreateDbContext() : null;
        return await new FiscalSettingsService(supplied ?? owned!, new StaticOperationalContextAccessor(Admin))
            .UpdateCompanySriSettingsAsync(new() { Environment = 1, EmissionType = 1, IsEnabled = true, AutomaticProcessingEnabled = enabled });
    }

    public SalesService Sales(TestServiceScope scope) => new(scope.DbContext, NullLogger<SalesService>.Instance,
        new StaticOperationalContextAccessor(Tenant.OperationalContext), scope.Inventory, scope.CashSessions,
        new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, new SriFiscalClock()),
        new SriAccessKeyService(), new SriXmlDraftService(), new SriFiscalClock(), new BusinessClockService(),
        new SriInvoiceXmlValidator(), Options, new TenantAdministrationGuard(scope.DbContext));

    public SaleCreateDto Request() => new()
    {
        RequestId = Guid.NewGuid(), DocumentType = SaleDocumentType.Invoice, CashReceived = 20m,
        Items = [new() { ProductId = Tenant.Products[0].Id, Quantity = 1, UnitPrice = 10 }]
    };

    public async Task<SaleDto> SellAsync(SaleCreateDto? request = null)
    {
        await using var scope = new TestServiceScope(database, Tenant.OperationalContext);
        return await Sales(scope).CreateAsync(request ?? Request());
    }

    public FiscalWorkerTestScope Worker(params IInterceptor[] interceptors) => new(database.CreateDbContext(interceptors), Protection, Transport, Options);

    public async Task<ElectronicIssuingJob> ClaimAsync()
    {
        await using var scope = Worker();
        return Assert.Single(await scope.Coordinator.ClaimBatchAsync(1, CancellationToken.None));
    }

    public async Task ProcessNextAsync()
    {
        var claim = await ClaimAsync();
        await using var scope = Worker();
        await scope.Processor.ProcessAsync(claim, CancellationToken.None);
    }

    public async Task DueAsync(int saleId, bool expire = false)
    {
        await using var db = database.CreateDbContext();
        if (expire)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ElectronicIssuingJobs\" SET \"LeaseExpiresAt\" = clock_timestamp() - interval '1 second', \"NextAttemptAt\" = clock_timestamp() WHERE \"SaleId\" = {saleId}");
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ElectronicIssuingJobs\" SET \"NextAttemptAt\" = clock_timestamp() WHERE \"SaleId\" = {saleId}");
    }
}

internal sealed class FiscalWorkerTestScope : IAsyncDisposable
{
    public PosDbContext Db { get; }
    public ElectronicIssuingCoordinator Coordinator { get; }
    public ElectronicIssuingProcessor Processor { get; }
    public SriSubmissionService Submission { get; }
    public SriInvoiceSigningService Signing { get; }
    public FiscalWorkerTestScope(PosDbContext db, IDataProtectionProvider protection, ISriWebServiceClient transport, IOptions<SriOptions> options,
        IOperationalContextAccessor? accessor = null)
    {
        Db = db; Coordinator = new(db, options);
        accessor ??= new NoHttpContext();
        var provider = new SriSigningCertificateProvider(db, accessor, protection, NullLogger<SriSigningCertificateProvider>.Instance);
        Signing = new(db, accessor, provider, new SriXadesBesSigner(), new SriInvoiceXmlValidator(), null!,
            NullLogger<SriInvoiceSigningService>.Instance, Coordinator);
        Submission = new(db, accessor, transport, null!, new SriInvoiceXmlValidator(), options,
            NullLogger<SriSubmissionService>.Instance, Coordinator);
        Processor = new(Coordinator, Signing, Submission);
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    private sealed class NoHttpContext : IOperationalContextAccessor
    {
        public Task<OperationalContext> GetRequiredContextAsync() => throw new InvalidOperationException("Worker must not request an HTTP identity.");
    }
}

internal sealed class SyntheticSriTransport : ISriWebServiceClient
{
    public int Submissions;
    public int Queries;
    public Func<CancellationToken, Task<SriReceptionResponse>>? Reception;
    public Func<CancellationToken, Task<SriAuthorizationResponse>>? Authorization;
    public string? LastKey;
    public string? SignedXml;
    public Task<SriReceptionResponse> SubmitAsync(string xml, int environment, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Submissions); SignedXml = xml;
        return Reception?.Invoke(cancellationToken) ?? Task.FromResult(new SriReceptionResponse { Estado = "RECIBIDA", RawResponseXml = "<recepcion/>" });
    }
    public Task<SriAuthorizationResponse> CheckAuthorizationAsync(string key, int environment, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Queries); LastKey = key;
        return Authorization?.Invoke(cancellationToken) ?? Task.FromResult(new SriAuthorizationResponse
        { Estado = "AUTORIZADO", AuthorizationNumber = key, AuthorizationDate = DateTime.UtcNow, RawResponseXml = "<autorizacion/>" });
    }
}
