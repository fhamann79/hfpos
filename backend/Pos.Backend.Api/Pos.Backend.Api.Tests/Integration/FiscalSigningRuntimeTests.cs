using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Xunit.Abstractions;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class FiscalSigningRuntimeTests(PostgresDatabaseFixture database, ITestOutputHelper output) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AutonomousIssuingFixture> Setup(string key = "rc6", int seed = 941, IDataProtectionProvider? protection = null)
    {
        var f = new AutonomousIssuingFixture(database, protection); await f.InitializeAsync(key, seed);
        await using var db = database.CreateDbContext();
        (await db.Companies.SingleAsync(c => c.Id == f.Tenant.CompanyId)).Ruc = seed == 942 ? SyntheticSigningCertificates.RucB : SyntheticSigningCertificates.RucA;
        await db.SaveChangesAsync();
        return f;
    }

    private async Task Production(AutonomousIssuingFixture f)
    {
        f.Options.Value.Environment = 2; f.Options.Value.AllowProductionSubmission = true;
        await using var db = database.CreateDbContext();
        (await db.CompanySriSettings.SingleAsync(s => s.CompanyId == f.Tenant.CompanyId)).Environment = 2;
        await db.SaveChangesAsync();
    }

    private async Task Upload(AutonomousIssuingFixture f, SyntheticSigningCertificates p)
    {
        using var intermediate = new X509Certificate2(p.Intermediate.RawData);
        using var root = new X509Certificate2(p.Root.RawData);
        using var stream = new MemoryStream(p.Pfx(p.Leaf, intermediate, root));
        var file = new FormFile(stream, 0, stream.Length, "certificate", "synthetic-rc6.pfx")
        { Headers = new HeaderDictionary(), ContentType = "application/x-pkcs12" };
        await using var db = database.CreateDbContext();
        await new SriCertificateService(db, new StaticOperationalContextAccessor(f.Admin), f.Protection,
            NullLogger<SriCertificateService>.Instance).UploadCertificateAsync(file, SyntheticSigningCertificates.Password);
    }

    [Theory]
    [InlineData("missing-guard")] [InlineData("disabled-options")] [InlineData("disabled-settings")]
    [InlineData("wrong-password")] [InlineData("corrupt-pfx")] [InlineData("expired")]
    [InlineData("future")] [InlineData("unsupported")] [InlineData("identity")]
    public async Task Production_rejects_before_sequence_stock_cash_or_durable_job(string failure)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p); await Production(f);
        if (failure == "disabled-options") f.Options.Value.AllowProductionSubmission = false;
        await using (var db = database.CreateDbContext())
        {
            if (failure == "disabled-settings") (await db.CompanySriSettings.SingleAsync()).IsEnabled = false;
            var row = await db.CompanySriCertificates.SingleAsync(c => c.IsActive);
            var protector = f.Protection.CreateProtector(SriCertificateProtectionPurposes.CertificateV1);
            if (failure == "wrong-password") row.EncryptedPassword = protector.Protect(Encoding.UTF8.GetBytes("synthetic-wrong"));
            if (failure == "corrupt-pfx") row.EncryptedCertificateBytes = protector.Protect(new byte[] { 1, 2, 3 });
            if (failure == "expired") row.NotAfter = DateTime.UtcNow.AddDays(-1);
            if (failure is "future" or "unsupported" or "identity")
            {
                using var cert = p.Issue("Synthetic invalid signer", p.Root,
                    ruc: failure == "identity" ? SyntheticSigningCertificates.RucB : SyntheticSigningCertificates.RucA,
                    keySize: failure == "unsupported" ? 1024 : 2048,
                    from: failure == "future" ? p.Now.AddDays(1) : null);
                row.EncryptedCertificateBytes = protector.Protect(p.Pfx(cert));
            }
            await db.SaveChangesAsync();
        }
        await using var scope = new TestServiceScope(database, f.Tenant.OperationalContext);
        var beforeStock = await scope.DbContext.ProductStocks.SumAsync(s => s.Quantity);
        var beforeSequence = await scope.DbContext.DocumentSequences.SumAsync(s => (int?)s.CurrentNumber) ?? 0;
        var beforeCash = await scope.DbContext.CashSessions.Select(s => new { s.Id, s.OpeningAmount }).ToListAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sales(scope, failure != "missing-guard").CreateWithPreviewAsync(f.Request()));
        scope.DbContext.ChangeTracker.Clear();
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.ElectronicIssuingJobs.ToListAsync());
        Assert.Equal(beforeStock, await scope.DbContext.ProductStocks.SumAsync(s => s.Quantity));
        Assert.Equal(beforeSequence, await scope.DbContext.DocumentSequences.SumAsync(s => (int?)s.CurrentNumber) ?? 0);
        Assert.Equal(beforeCash, await scope.DbContext.CashSessions.Select(s => new { s.Id, s.OpeningAmount }).ToListAsync());
    }

    [Fact]
    public async Task Committed_replay_does_not_load_replaced_certificate_or_require_guard()
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p); await Production(f);
        var request = f.Request(); SaleDto sale;
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            sale = await f.Sales(scope).CreateWithPreviewAsync(request);
        await using (var db = database.CreateDbContext())
        { (await db.CompanySriCertificates.SingleAsync(c => c.IsActive)).EncryptedCertificateBytes = [7, 8, 9]; await db.SaveChangesAsync(); }
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            Assert.Equal(sale.Id, (await f.Sales(scope, false).CreateAsync(request)).Id);
        await using var verify = database.CreateDbContext(); Assert.Equal(1, await verify.Sales.CountAsync());
        Assert.Equal(1, await verify.DocumentSequences.SumAsync(s => s.CurrentNumber));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Production_sign_reception_rotation_and_historical_authorization_without_private_material(bool manual)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p); await Production(f);
        var sale = await f.SellAsync(); await f.ProcessNextAsync();
        string signed;
        await using (var db = database.CreateDbContext()) signed = (await db.Sales.SingleAsync()).SriSignedXml!;
        using var rotated = new SyntheticSigningCertificates(rootName: "Synthetic rotated root");
        await Upload(f, rotated);
        if (manual)
        {
            await using var views = new TestServiceScope(database, f.Admin);
            await using var worker = new FiscalWorkerTestScope(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
                new StaticOperationalContextAccessor(f.Admin), f.Sales(views));
            Assert.Equal(sale.Id, (await worker.Submission.SubmitSignedInvoiceAsync(sale.Id)).Id);
        }
        else await f.ProcessNextAsync();
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(signed, f.Transport.SignedXml);
        await using (var db = database.CreateDbContext())
        { (await db.CompanySriCertificates.SingleAsync(c => c.IsActive)).EncryptedCertificateBytes = [7, 8, 9]; await db.SaveChangesAsync(); }
        await f.DueAsync(sale.Id); await f.ProcessNextAsync();
        Assert.Equal(1, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        Assert.Equal(signed, (await verify.Sales.SingleAsync()).SriSignedXml);
        Assert.Equal(SaleDocumentStatus.Authorized, (await verify.Sales.SingleAsync()).DocumentStatus);
        Assert.NotNull((await verify.ElectronicIssuingJobs.SingleAsync()).ReceptionStartedAt);
    }

    [Theory]
    [InlineData(1, "hash")] [InlineData(2, "hash")]
    [InlineData(1, "crypto")] [InlineData(2, "crypto")]
    [InlineData(1, "draft")] [InlineData(2, "draft")]
    [InlineData(1, "key")] [InlineData(2, "key")]
    [InlineData(2, "settings")]
    public async Task Local_reception_failure_has_zero_transport_calls_and_no_false_ambiguity(int environment, string failure)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p);
        if (environment == 2) await Production(f);
        await f.SellAsync(); await f.ProcessNextAsync();
        await using (var db = database.CreateDbContext())
        {
            var stored = await db.Sales.SingleAsync();
            if (failure == "hash") stored.SriSignatureHash = "00";
            if (failure == "crypto")
            {
                stored.SriSignedXml = stored.SriSignedXml!.Replace("<ruc>", "<ruc>9");
                stored.SriSignatureHash = Hash(stored.SriSignedXml);
            }
            if (failure == "draft")
            {
                stored.SriXmlDraft = "<factura/>";
                (await db.ElectronicIssuingJobs.SingleAsync()).DraftHash = Hash(stored.SriXmlDraft).ToLowerInvariant();
            }
            if (failure == "key")
            {
                stored.AccessKey = new string('1', 49);
                (await db.ElectronicIssuingJobs.SingleAsync()).AccessKey = stored.AccessKey;
            }
            if (failure == "settings") (await db.CompanySriSettings.SingleAsync()).IsEnabled = false;
            await db.SaveChangesAsync();
        }
        await f.ProcessNextAsync();
        Assert.Equal(0, f.Transport.Submissions);
        await using var verify = database.CreateDbContext();
        Assert.Null((await verify.ElectronicIssuingJobs.SingleAsync()).ReceptionStartedAt);
        Assert.Null((await verify.Sales.SingleAsync()).SriReceptionStatus);
    }

    [Theory]
    [InlineData("DEVUELTA")] [InlineData("AMBIGUOUS")]
    public async Task Mock_rejection_or_ambiguous_reception_preserves_document_and_number(string outcome)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p); await Production(f);
        var sale = await f.SellAsync(); await f.ProcessNextAsync();
        f.Transport.Reception = _ => Task.FromResult(new SriReceptionResponse { Estado = outcome, RawResponseXml = "<synthetic-reception/>",
            Messages = [new() { Identifier = "39", Type = "ERROR", Message = "Synthetic signature rejection" }] });
        await f.ProcessNextAsync();
        await using var db = database.CreateDbContext();
        var stored = await db.Sales.SingleAsync(); var job = await db.ElectronicIssuingJobs.SingleAsync();
        var attempt = await db.SriSubmissionAttempts.SingleAsync();
        Assert.Equal(outcome, attempt.ReceptionStatus); Assert.Equal("39", attempt.SriMessageIdentifier);
        Assert.Equal("<synthetic-reception/>", attempt.ResponseXml);
        Assert.Equal(sale.AccessKey, stored.AccessKey); Assert.Equal(sale.Number, stored.Number);
        Assert.Equal(1, await db.DocumentSequences.SumAsync(s => s.CurrentNumber));
        Assert.NotNull(job.ReceptionStartedAt);
        if (outcome == "DEVUELTA")
        {
            Assert.Equal(SaleDocumentStatus.Rejected, stored.DocumentStatus);
            Assert.Equal(ElectronicIssuingPhase.Completed, job.Phase);
            Assert.Equal("SRI_RECEPTION_REJECTED", attempt.ErrorCode);
        }
        else
        {
            Assert.Equal(ElectronicIssuingPhase.UnknownReception, job.Phase);
            await f.DueAsync(sale.Id); await f.ProcessNextAsync();
            Assert.Equal(1, f.Transport.Queries);
        }
        await f.DueAsync(sale.Id);
        await using var worker = f.Worker(); Assert.Empty(await worker.Coordinator.ClaimBatchAsync(1, CancellationToken.None));
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(1, await db.Sales.CountAsync());
    }

    [Fact]
    public async Task Two_tenants_with_different_uninstalled_authorities_sign_independently_and_keep_ciphertext()
    {
        var a = await Setup("rc6a", 941); var b = await Setup("rc6b", 942, a.Protection);
        using var p = new SyntheticSigningCertificates(); using var q = new SyntheticSigningCertificates(SyntheticSigningCertificates.RucB, "Synthetic other CA");
        await Upload(a, p); await Upload(b, q); await Production(a); await Production(b);
        await using (var db = database.CreateDbContext())
        {
            var row = await db.CompanySriCertificates.AsNoTracking().SingleAsync(c => c.CompanyId == a.Tenant.CompanyId && c.IsActive);
            var cipher = row.EncryptedCertificateBytes.ToArray();
            using var material = await new SriSigningCertificateProvider(db, new StaticOperationalContextAccessor(a.Admin), a.Protection,
                NullLogger<SriSigningCertificateProvider>.Instance).GetActiveCertificateMaterialAsync();
            Assert.Equal(p.Leaf.Thumbprint, material.Certificate.Thumbprint);
            Assert.Equal(cipher, (await db.CompanySriCertificates.AsNoTracking().SingleAsync(c => c.Id == row.Id)).EncryptedCertificateBytes);
        }
        var saleA = await a.SellAsync(); var saleB = await b.SellAsync();
        await a.ProcessNextAsync(); await a.ProcessNextAsync();
        a.Transport.Reception = _ => Task.FromResult(new SriReceptionResponse { Estado = "DEVUELTA", RawResponseXml = "<synthetic-rejection/>" });
        await a.ProcessNextAsync(); await b.ProcessNextAsync();
        await using var verify = database.CreateDbContext();
        var first = await verify.Sales.SingleAsync(s => s.Id == saleA.Id); var second = await verify.Sales.SingleAsync(s => s.Id == saleB.Id);
        Assert.Equal(p.Leaf.Thumbprint, first.SriSigningCertificateThumbprint);
        Assert.Equal(q.Leaf.Thumbprint, second.SriSigningCertificateThumbprint);
        Assert.Equal(SaleDocumentStatus.Rejected, first.DocumentStatus);
        Assert.Equal(SaleDocumentStatus.PendingAuthorization, second.DocumentStatus);
        Assert.Equal(1, a.Transport.Submissions); Assert.Equal(1, b.Transport.Submissions);
        Assert.Contains(SyntheticSigningCertificates.RucA, first.SriSignedXml); Assert.Contains(SyntheticSigningCertificates.RucB, second.SriSignedXml);
        // Shared keyring is not authority to select another company's certificate.
        using var own = await new SriSigningCertificateProvider(verify, new StaticOperationalContextAccessor(b.Admin), b.Protection,
            NullLogger<SriSigningCertificateProvider>.Instance).GetActiveCertificateMaterialAsync();
        Assert.Equal(q.Leaf.Thumbprint, own.Certificate.Thumbprint);
    }

    [Theory]
    [InlineData("native")] [InlineData("exception")] [InlineData("success")]
    public async Task PartialChain_readiness_is_production_ready_with_consistent_counters_and_no_writes(string outcome)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p); await Production(f);
        var noWrites = new RejectWrites(); await using var db = database.CreateDbContext(noWrites);
        var readiness = await Readiness(f, db, outcome).GetReadinessAsync();
        var code = outcome == "exception" ? "CERTIFICATE_CHAIN_DIAGNOSTIC_FAILED" : outcome == "success" ? "CERTIFICATE_CHAIN_BUILD_OK" : "CERTIFICATE_CHAIN_BUILD_FAILED";
        var chain = Assert.Single(readiness.Checks.Where(c => c.Code == code));
        if (outcome == "native") Assert.Contains("PartialChain", chain.Details);
        Assert.Equal(outcome == "success" ? "success" : "warning", chain.Severity); Assert.False(chain.IsBlocking);
        Assert.True(readiness.IsReadyForProductionSubmission, string.Join(",", readiness.Checks.Where(c => c.IsBlocking).Select(c => c.Code)));
        Assert.False(readiness.HasBlockingErrors); Assert.Equal(0, readiness.BlockingErrorCount);
        Assert.Equal(readiness.Checks.Count(c => c.Severity == "warning"), readiness.WarningCount);
        Assert.Equal(readiness.WarningCount > 0, readiness.HasWarnings); Assert.Equal(0, noWrites.Writes);
        output.WriteLine($"{code}={chain.Severity}; IsReadyForProductionSubmission=true; BlockingErrorCount=0; WarningCount={readiness.WarningCount}; writes=0");
    }

    [Fact]
    public async Task Tenant_readiness_does_not_create_absent_settings()
    {
        var f = await Setup();
        await using (var db = database.CreateDbContext())
        { db.CompanySriSettings.RemoveRange(await db.CompanySriSettings.ToListAsync()); await db.SaveChangesAsync(); }
        var noWrites = new RejectWrites(); await using var read = database.CreateDbContext(noWrites);
        var result = await Readiness(f, read).GetReadinessAsync();
        Assert.False(result.IsReadyForSandboxSubmission); Assert.False(result.IsReadyForProductionSubmission);
        Assert.Contains(result.Checks, c => c.Code == "SRI_SETTINGS_MISSING");
        Assert.Equal(0, noWrites.Writes); Assert.Empty(await read.CompanySriSettings.ToListAsync());
    }

    [Theory]
    [InlineData("CERTIFICATE_SIGNER_AMBIGUOUS")] [InlineData("CERTIFICATE_COLLECTION_LIMIT")]
    [InlineData("CERTIFICATE_IMPORT_CAPACITY")] [InlineData("CERTIFICATE_NOT_YET_VALID")]
    [InlineData("unrecognized-operation")] [InlineData("unexpected-exception")]
    public async Task Production_readiness_material_failures_are_blocking_and_sanitized(string failure)
    {
        var f = await Setup(); await Production(f);
        var noWrites = new RejectWrites(); await using var db = database.CreateDbContext(noWrites);
        var readiness = await new SriFiscalReadinessService(db, new StaticOperationalContextAccessor(f.Admin),
            new FailingCertificateProvider(failure), f.Options, NullLogger<SriFiscalReadinessService>.Instance).GetReadinessAsync();
        var code = failure.StartsWith("CERTIFICATE_", StringComparison.Ordinal) ? failure : "CERTIFICATE_DIAGNOSTIC_FAILED";
        Assert.Contains(readiness.Checks, c => c.Code == code && c.IsBlocking && c.Severity == "error");
        Assert.True(readiness.HasBlockingErrors); Assert.False(readiness.IsReadyForProductionSubmission);
        Assert.DoesNotContain(readiness.Checks, c => (c.Details ?? "").Contains("synthetic-private-detail") || c.Message.Contains("synthetic-private-detail"));
        Assert.Equal(0, noWrites.Writes);
    }

    [Fact]
    public async Task Ambiguous_upload_preserves_active_cipher_and_settings()
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p);
        await using var db = database.CreateDbContext();
        var before = await db.CompanySriCertificates.AsNoTracking().SingleAsync(c => c.IsActive);
        var settings = await db.CompanySriSettings.AsNoTracking().SingleAsync();
        using var stream = new MemoryStream(p.Pfx(p.Leaf, p.Intermediate));
        var file = new FormFile(stream, 0, stream.Length, "certificate", "synthetic-ambiguous.pfx");
        var service = new SriCertificateService(db, new StaticOperationalContextAccessor(f.Admin), f.Protection, NullLogger<SriCertificateService>.Instance);
        Assert.Equal("CERTIFICATE_SIGNER_AMBIGUOUS", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadCertificateAsync(file, SyntheticSigningCertificates.Password))).Message);
        db.ChangeTracker.Clear(); var current = await db.CompanySriCertificates.SingleAsync(c => c.IsActive);
        Assert.Equal(before.Id, current.Id); Assert.Equal(before.EncryptedCertificateBytes, current.EncryptedCertificateBytes);
        Assert.Equal(settings.UpdatedAt, (await db.CompanySriSettings.SingleAsync()).UpdatedAt);
    }

    [Theory]
    [InlineData(1, "positive")] [InlineData(2, "positive")]
    [InlineData(1, "corrupt-xml")] [InlineData(2, "corrupt-xml")]
    [InlineData(2, "null-environment")]
    public async Task Credit_note_signing_reception_and_history_preserve_integrity(int environment, string scenario)
    {
        var f = await Setup(); using var p = new SyntheticSigningCertificates(); await Upload(f, p);
        if (environment == 2) await Production(f);
        var sale = await f.SellAsync();
        await using var db = database.CreateDbContext();
        var original = await db.Sales.SingleAsync(); original.DocumentStatus = SaleDocumentStatus.Authorized;
        original.AuthorizationNumber = original.AccessKey; original.AuthorizedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        var accessor = new StaticOperationalContextAccessor(f.Admin); var clock = new BusinessClockService();
        var notes = new CreditNoteService(db, accessor,
            new FiscalDocumentNumberService(db, NullLogger<FiscalDocumentNumberService>.Instance, new SriFiscalClock()), clock,
            new SriAccessKeyService(), new SriCreditNoteXmlDraftService(), new SriCreditNoteXmlValidator(), new SriFiscalClock(),
            f.Options, NullLogger<CreditNoteService>.Instance, new TenantAdministrationGuard(db));
        var note = await notes.CreateDraftAsync(new() { OriginalSaleId = sale.Id, Reason = "Synthetic RC6 return",
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1 }] });
        await notes.PrepareSriDraftAsync(note.Id);
        var provider = new SriSigningCertificateProvider(db, accessor, f.Protection, NullLogger<SriSigningCertificateProvider>.Instance);
        var signing = new SriCreditNoteSigningService(db, accessor, provider, new SriXadesBesSigner(), new SriCreditNoteXmlValidator(),
            notes, clock, NullLogger<SriCreditNoteSigningService>.Instance);
        if (scenario == "null-environment")
        {
            (await db.CreditNotes.SingleAsync()).SriEnvironment = null; await db.SaveChangesAsync();
            Assert.Equal("INVALID_SRI_ENVIRONMENT", (await Assert.ThrowsAsync<InvalidOperationException>(() => signing.SignDraftAsync(note.Id))).Message);
            Assert.Null((await db.CreditNotes.AsNoTracking().SingleAsync()).SriSignedXml); return;
        }
        await signing.SignDraftAsync(note.Id);
        var signed = (await db.CreditNotes.AsNoTracking().SingleAsync()).SriSignedXml; Assert.NotNull(signed);
        if (scenario == "corrupt-xml") { (await db.CreditNotes.SingleAsync()).SriSignatureHash = "00"; await db.SaveChangesAsync(); }
        var submission = new SriCreditNoteSubmissionService(db, accessor, f.Transport, notes, new SriCreditNoteXmlValidator(),
            clock, f.Options, NullLogger<SriCreditNoteSubmissionService>.Instance);
        if (scenario == "positive")
        {
            await submission.SubmitSignedAsync(note.Id); Assert.Equal(1, f.Transport.Submissions); Assert.Equal(signed, f.Transport.SignedXml);
            var replaySigner = new SriCreditNoteSigningService(db, accessor, null!, null!, new SriCreditNoteXmlValidator(), notes,
                clock, NullLogger<SriCreditNoteSigningService>.Instance);
            Assert.Equal(note.Id, (await replaySigner.SignDraftAsync(note.Id)).Id);
            (await db.CompanySriCertificates.SingleAsync(c => c.IsActive)).EncryptedCertificateBytes = [7, 8, 9];
            await db.SaveChangesAsync();
            f.Transport.Authorization = _ => Task.FromResult(new SriAuthorizationResponse
            {
                Estado = "AUTORIZADO", AuthorizationNumber = note.AccessKey, AuthorizationDate = DateTime.UtcNow,
                AuthorizedXml = signed,
                RawResponseXml = new XElement("autorizacion", new XElement("estado", "AUTORIZADO"),
                    new XElement("numeroAutorizacion", note.AccessKey), new XElement("comprobante", new XCData(signed!)))
                    .ToString(SaveOptions.DisableFormatting)
            });
            Assert.Equal(SaleDocumentStatus.Authorized, (await submission.CheckAuthorizationAsync(note.Id)).DocumentStatus);
            Assert.Equal(1, f.Transport.Queries); Assert.Equal(1, f.Transport.Submissions);
        }
        else
        {
            Assert.Equal("FISCAL_SIGNED_XML_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => submission.SubmitSignedAsync(note.Id))).Message);
            Assert.Equal(0, f.Transport.Submissions);
            Assert.Empty(await db.SriSubmissionAttempts.Where(a => a.CreditNoteId == note.Id).ToListAsync());
        }
        Assert.Equal(signed, await signing.GetSignedXmlAsync(note.Id));
    }

    private static SriFiscalReadinessService Readiness(AutonomousIssuingFixture f, Api.Infrastructure.Data.PosDbContext db, string outcome = "native")
        => new DiagnosticReadiness(db, new StaticOperationalContextAccessor(f.Admin), new SriSigningCertificateProvider(db,
            new StaticOperationalContextAccessor(f.Admin), f.Protection, NullLogger<SriSigningCertificateProvider>.Instance),
            f.Options, outcome);
    private sealed class DiagnosticReadiness(Api.Infrastructure.Data.PosDbContext db, IOperationalContextAccessor accessor,
        ISriSigningCertificateProvider provider, IOptions<SriOptions> options, string outcome)
        : SriFiscalReadinessService(db, accessor, provider, options, NullLogger<SriFiscalReadinessService>.Instance)
    {
        internal override bool BuildLocalChain(X509Chain chain, X509Certificate2 certificate)
            => outcome == "exception" ? throw new CryptographicException("synthetic-private-detail")
                : outcome == "success" || base.BuildLocalChain(chain, certificate);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed class FailingCertificateProvider(string failure) : ISriSigningCertificateProvider
    {
        public Task<SriSigningCertificateMaterial> GetActiveCertificateMaterialAsync()
            => failure == "unexpected-exception" ? Task.FromException<SriSigningCertificateMaterial>(new CryptographicException("synthetic-private-detail"))
                : Task.FromException<SriSigningCertificateMaterial>(new InvalidOperationException(failure == "unrecognized-operation" ? "synthetic-private-detail" : failure));
    }
    private sealed class RejectWrites : DbCommandInterceptor
    {
        public int Writes;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Read-only test forbids writes."); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            { Writes++; throw new InvalidOperationException("Read-only test forbids reader writes."); }
            return ValueTask.FromResult(result);
        }
    }
}
