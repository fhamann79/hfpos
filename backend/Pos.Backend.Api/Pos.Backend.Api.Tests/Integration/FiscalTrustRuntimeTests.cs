using System.Data.Common;
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
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class FiscalTrustRuntimeTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AutonomousIssuingFixture> Setup(string key = "rc6", int seed = 941, IDataProtectionProvider? protection = null)
    {
        var f = new AutonomousIssuingFixture(database, protection); await f.InitializeAsync(key, seed);
        await using var db = database.CreateDbContext();
        (await db.Companies.SingleAsync(c => c.Id == f.Tenant.CompanyId)).Ruc = seed == 942 ? FiscalTrustSyntheticPki.RucB : FiscalTrustSyntheticPki.RucA;
        await db.SaveChangesAsync();
        return f;
    }

    private async Task Production(AutonomousIssuingFixture f)
    {
        f.Options.Value.Environment = 2; f.Options.Value.AllowProductionSubmission = true;
        await using var db = database.CreateDbContext();
        var settings = await db.CompanySriSettings.SingleAsync(s => s.CompanyId == f.Tenant.CompanyId);
        settings.Environment = 2; await db.SaveChangesAsync();
    }

    private async Task Upload(AutonomousIssuingFixture f, FiscalTrustSyntheticPki p)
    {
        using var intermediate = new X509Certificate2(p.Intermediate.RawData);
        using var root = new X509Certificate2(p.Root.RawData);
        using var stream = new MemoryStream(p.Pfx(p.Leaf, intermediate, root));
        var file = new FormFile(stream, 0, stream.Length, "certificate", "synthetic-rc6.pfx")
        { Headers = new HeaderDictionary(), ContentType = "application/x-pkcs12" };
        await using var db = database.CreateDbContext();
        await new SriCertificateService(db, new StaticOperationalContextAccessor(f.Admin), f.Protection,
            NullLogger<SriCertificateService>.Instance).UploadCertificateAsync(file, "synthetic-rc6-only");
    }

    private FiscalWorkerTestScope Worker(AutonomousIssuingFixture f, IFiscalCertificateEvaluator evaluator,
        bool manual = false) => new(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
            manual ? new StaticOperationalContextAccessor(f.Admin) : null, evaluator, f.PublicChains);

    [Theory]
    [InlineData("missing-guard")] [InlineData("unapproved-registry")] [InlineData("disabled-options")]
    [InlineData("disabled-settings")] [InlineData("revoked")]
    public async Task Production_admission_rejects_before_sequence_stock_cash_or_durable_job(string failure)
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p); await Production(f);
        if (failure == "revoked") p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf);
        IFiscalCertificateEvaluator? evaluator = failure == "missing-guard" ? null : new FiscalCertificateEvaluator(
            failure == "unapproved-registry" ? FiscalTrustRegistry.Load([1], new string('0', 64)) : p.Registry());
        if (failure == "disabled-options") f.Options.Value.AllowProductionSubmission = false;
        if (failure == "disabled-settings")
        {
            await using var db = database.CreateDbContext(); (await db.CompanySriSettings.SingleAsync()).IsEnabled = false; await db.SaveChangesAsync();
        }
        await using var scope = new TestServiceScope(database, f.Tenant.OperationalContext);
        var beforeStock = await scope.DbContext.ProductStocks.SumAsync(s => s.Quantity);
        var beforeSequence = await scope.DbContext.DocumentSequences.SumAsync(s => (int?)s.CurrentNumber) ?? 0;
        var beforeCash = await scope.DbContext.CashSessions.Select(s => new { s.Id, s.OpeningAmount }).ToListAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sales(scope, evaluator).CreateWithPreviewAsync(f.Request()));
        scope.DbContext.ChangeTracker.Clear();
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.ElectronicIssuingJobs.ToListAsync());
        Assert.Equal(beforeStock, await scope.DbContext.ProductStocks.SumAsync(s => s.Quantity));
        Assert.Equal(beforeSequence, await scope.DbContext.DocumentSequences.SumAsync(s => (int?)s.CurrentNumber) ?? 0);
        Assert.Equal(beforeCash, await scope.DbContext.CashSessions.Select(s => new { s.Id, s.OpeningAmount }).ToListAsync());
    }

    [Fact]
    public async Task Committed_replay_survives_trust_withdrawal_and_no_guard_constructor()
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p); await Production(f);
        var request = f.Request(); SaleDto sale;
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            sale = await f.Sales(scope, new FiscalCertificateEvaluator(p.Registry())).CreateWithPreviewAsync(request);
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            Assert.Equal(sale.Id, (await f.Sales(scope).CreateAsync(request)).Id);
        await using var db = database.CreateDbContext(); Assert.Equal(1, await db.Sales.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_production_sign_reception_rotation_and_historical_authorization(bool manual)
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p); await Production(f);
        var evaluator = new FiscalCertificateEvaluator(p.Registry());
        SaleDto sale;
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            sale = await f.Sales(scope, evaluator).CreateWithPreviewAsync(f.Request());
        var signClaim = await f.ClaimAsync();
        await using (var worker = Worker(f, evaluator)) await worker.Processor.ProcessAsync(signClaim, CancellationToken.None);
        string signed;
        await using (var db = database.CreateDbContext()) signed = (await db.Sales.SingleAsync()).SriSignedXml!;
        using var rotated = new FiscalTrustSyntheticPki(rootName: "synthetic rotated root");
        // Operational active-certificate barrier remains. Reception must still validate the OLD signer.
        await Upload(f, rotated);
        if (manual)
        {
            await using var views = new TestServiceScope(database, f.Admin);
            await using var worker = new FiscalWorkerTestScope(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
                new StaticOperationalContextAccessor(f.Admin), evaluator, f.PublicChains, f.Sales(views, evaluator));
            Assert.Equal(sale.Id, (await worker.Submission.SubmitSignedInvoiceAsync(sale.Id)).Id);
        }
        else
        {
            var submitClaim = await f.ClaimAsync();
            await using var worker = Worker(f, evaluator); await worker.Processor.ProcessAsync(submitClaim, CancellationToken.None);
        }
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(signed, f.Transport.SignedXml);
        await f.DueAsync(sale.Id);
        var queryClaim = await f.ClaimAsync();
        await using (var worker = Worker(f, new NeverEvaluate())) await worker.Processor.ProcessAsync(queryClaim, CancellationToken.None);
        Assert.Equal(1, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        Assert.Equal(signed, (await verify.Sales.SingleAsync()).SriSignedXml);
        Assert.NotNull((await verify.ElectronicIssuingJobs.SingleAsync()).ReceptionStartedAt);
    }

    [Theory]
    [InlineData("hash")] [InlineData("crypto")] [InlineData("draft")]
    [InlineData("revoked")] [InlineData("settings")]
    public async Task Local_reception_failure_has_zero_transport_calls_and_no_false_ambiguity(string failure)
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p); await Production(f);
        var evaluator = new FiscalCertificateEvaluator(p.Registry()); SaleDto sale;
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            sale = await f.Sales(scope, evaluator).CreateWithPreviewAsync(f.Request());
        var claim = await f.ClaimAsync();
        await using (var worker = Worker(f, evaluator)) await worker.Processor.ProcessAsync(claim, CancellationToken.None);
        await using (var db = database.CreateDbContext())
        {
            var stored = await db.Sales.SingleAsync();
            if (failure == "hash") stored.SriSignatureHash = "00";
            if (failure == "crypto")
            {
                stored.SriSignedXml = stored.SriSignedXml!.Replace("<ruc>", "<ruc>9");
                stored.SriSignatureHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stored.SriSignedXml)));
            }
            if (failure == "draft")
            {
                stored.SriXmlDraft = "<factura/>";
                (await db.ElectronicIssuingJobs.SingleAsync()).DraftHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stored.SriXmlDraft))).ToLowerInvariant();
            }
            if (failure == "settings") (await db.CompanySriSettings.SingleAsync()).IsEnabled = false;
            await db.SaveChangesAsync();
        }
        if (failure == "revoked") { p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf); evaluator = new FiscalCertificateEvaluator(p.Registry()); }
        var submit = await f.ClaimAsync();
        await using (var worker = Worker(f, evaluator)) await worker.Processor.ProcessAsync(submit, CancellationToken.None);
        Assert.Equal(0, f.Transport.Submissions);
        await using var verify = database.CreateDbContext();
        Assert.Null((await verify.ElectronicIssuingJobs.SingleAsync()).ReceptionStartedAt);
        Assert.Null((await verify.Sales.SingleAsync()).SriReceptionStatus);
    }

    [Fact]
    public async Task Ambiguous_upload_preserves_active_cipher_and_settings()
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p);
        await using var db = database.CreateDbContext();
        var before = await db.CompanySriCertificates.AsNoTracking().SingleAsync(c => c.IsActive);
        var settings = await db.CompanySriSettings.AsNoTracking().SingleAsync();
        using var stream = new MemoryStream(p.Pfx(p.Leaf, p.Intermediate));
        var file = new FormFile(stream, 0, stream.Length, "certificate", "ambiguous.pfx");
        var service = new SriCertificateService(db, new StaticOperationalContextAccessor(f.Admin), f.Protection, NullLogger<SriCertificateService>.Instance);
        Assert.Equal("CERTIFICATE_SIGNER_AMBIGUOUS", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadCertificateAsync(file, "synthetic-rc6-only"))).Message);
        db.ChangeTracker.Clear();
        var current = await db.CompanySriCertificates.SingleAsync(c => c.IsActive);
        Assert.Equal(before.Id, current.Id); Assert.Equal(before.EncryptedCertificateBytes, current.EncryptedCertificateBytes);
        Assert.Equal(settings.UpdatedAt, (await db.CompanySriSettings.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task Monitor_covers_all_pages_is_read_only_and_platform_never_decrypts_or_evaluates()
    {
        var a = await Setup("rc6a", 941); var b = await Setup("rc6b", 942, a.Protection);
        using var p = new FiscalTrustSyntheticPki(); await Upload(a, p);
        using var p2 = new FiscalTrustSyntheticPki(FiscalTrustSyntheticPki.RucB, "synthetic other root"); await Upload(b, p2);
        p.Manifest.Authorities.AddRange(p2.Manifest.Authorities); p.Manifest.Crls.AddRange(p2.Manifest.Crls);
        var registry = p.Registry(); var options = Options.Create(new FiscalTrustOptions { MonitorBatchSize = 1 });
        var cache = new FiscalPreflightCache(options); var chains = new FiscalPublicChainCache();
        var noWrites = new RejectWrites(); await using var db = database.CreateDbContext(noWrites);
        var beforeSettings = await db.CompanySriSettings.CountAsync();
        var crypto = new CountingEvaluator(new FiscalCertificateEvaluator(registry));
        await new FiscalTrustMonitor(db, a.Protection, crypto, cache, chains, options).RunCycleAsync(CancellationToken.None);
        var evaluationsBeforePlatform = crypto.Calls;
        var platform = new PlatformFiscalTrustService(db, new PlatformActor(), registry, cache);
        var first = await platform.PreflightAsync(1, 1, CancellationToken.None);
        var second = await platform.PreflightAsync(2, 1, CancellationToken.None);
        Assert.Equal(2, first.TotalCompanies); Assert.Equal(2, first.TotalPages); Assert.NotNull(first.LastCompletedCycleAt);
        Assert.Equal("CANDIDATE_ELIGIBLE", Assert.Single(first.Items).State);
        Assert.Equal("CANDIDATE_ELIGIBLE", Assert.Single(second.Items).State);
        Assert.Equal(beforeSettings, await db.CompanySriSettings.CountAsync());
        Assert.Equal(evaluationsBeforePlatform, crypto.Calls);
        // A later bad ciphertext is isolated; the other tenant remains eligible.
        await using (var mutate = database.CreateDbContext())
        {
            (await mutate.CompanySriCertificates.SingleAsync(c => c.CompanyId == b.Tenant.CompanyId && c.IsActive)).EncryptedCertificateBytes = [7, 8, 9];
            await mutate.SaveChangesAsync();
        }
        await new FiscalTrustMonitor(db, a.Protection, crypto, cache, chains, options).RunCycleAsync(CancellationToken.None);
        Assert.Equal("CANDIDATE_ELIGIBLE", Assert.Single((await platform.PreflightAsync(1, 1, CancellationToken.None)).Items).State);
        Assert.Equal("UNKNOWN", Assert.Single((await platform.PreflightAsync(2, 1, CancellationToken.None)).Items).State);
        var evaluationsAfterSecondCycle = crypto.Calls;
        await using (var mutate = database.CreateDbContext())
        {
            var cert = await mutate.CompanySriCertificates.SingleAsync(c => c.CompanyId == a.Tenant.CompanyId && c.IsActive);
            cert.EncryptedCertificateBytes = [4, 5, 6]; await mutate.SaveChangesAsync();
        }
        var changed = await platform.PreflightAsync(1, 1, CancellationToken.None);
        Assert.Equal("UNKNOWN", Assert.Single(changed.Items).State);
        Assert.Equal("FISCAL_PREFLIGHT_SNAPSHOT_CHANGED", Assert.Single(changed.Items).Reason);
        Assert.Equal(0, noWrites.Writes);
        Assert.Equal(evaluationsAfterSecondCycle, crypto.Calls);
    }

    [Fact]
    public async Task Tenant_readiness_does_not_create_absent_settings()
    {
        var f = await Setup();
        await using (var db = database.CreateDbContext())
        { db.CompanySriSettings.RemoveRange(await db.CompanySriSettings.ToListAsync()); await db.SaveChangesAsync(); }
        var noWrites = new RejectWrites(); await using var read = database.CreateDbContext(noWrites);
        var provider = new SriSigningCertificateProvider(read, new StaticOperationalContextAccessor(f.Admin), f.Protection, NullLogger<SriSigningCertificateProvider>.Instance);
        await new SriFiscalReadinessService(read, new StaticOperationalContextAccessor(f.Admin), provider, f.Options,
            NullLogger<SriFiscalReadinessService>.Instance).GetReadinessAsync();
        Assert.Equal(0, noWrites.Writes); Assert.Empty(await read.CompanySriSettings.ToListAsync());
    }

    [Theory]
    [InlineData("CERTIFICATE_SIGNER_AMBIGUOUS")]
    [InlineData("CERTIFICATE_COLLECTION_LIMIT")]
    [InlineData("CERTIFICATE_IMPORT_CAPACITY")]
    [InlineData("CERTIFICATE_NOT_YET_VALID")]
    [InlineData("unrecognized-operation")]
    [InlineData("unexpected-exception")]
    public async Task Production_readiness_material_failures_are_blocking_and_sanitized(string failure)
    {
        var f = await Setup();
        await Production(f);
        var noWrites = new RejectWrites();
        await using var db = database.CreateDbContext(noWrites);
        var readiness = await new SriFiscalReadinessService(db, new StaticOperationalContextAccessor(f.Admin),
            new FailingCertificateProvider(failure), f.Options, NullLogger<SriFiscalReadinessService>.Instance)
            .GetReadinessAsync();

        var code = failure.StartsWith("CERTIFICATE_", StringComparison.Ordinal)
            ? failure : "CERTIFICATE_DIAGNOSTIC_FAILED";
        var check = Assert.Single(readiness.Checks.Where(c => c.Code == code));
        Assert.True(check.IsBlocking);
        Assert.Equal("error", check.Severity);
        Assert.True(readiness.HasBlockingErrors);
        Assert.False(readiness.IsReadyForProductionSubmission);
        Assert.DoesNotContain(readiness.Checks, c =>
            (c.Details ?? "").Contains("synthetic-private-detail", StringComparison.Ordinal)
            || c.Message.Contains("synthetic-private-detail", StringComparison.Ordinal));
        Assert.Equal(0, noWrites.Writes);
    }

    [Theory]
    [InlineData("positive")] [InlineData("null-environment")]
    [InlineData("revoked")] [InlineData("corrupt-xml")]
    public async Task Credit_note_actual_signer_and_reception_guards_preserve_historical_signature(string scenario)
    {
        var f = await Setup(); using var p = new FiscalTrustSyntheticPki(); await Upload(f, p); await Production(f);
        var evaluator = new FiscalCertificateEvaluator(p.Registry()); SaleDto sale;
        await using (var scope = new TestServiceScope(database, f.Tenant.OperationalContext))
            sale = await f.Sales(scope, evaluator).CreateWithPreviewAsync(f.Request());
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
        var guard = new FiscalExecutionGuard(db, provider, evaluator, f.PublicChains);
        var signing = new SriCreditNoteSigningService(db, accessor, provider, new SriXadesBesSigner(), new SriCreditNoteXmlValidator(),
            notes, clock, NullLogger<SriCreditNoteSigningService>.Instance, guard);
        if (scenario == "null-environment")
        {
            (await db.CreditNotes.SingleAsync()).SriEnvironment = null; await db.SaveChangesAsync();
            Assert.Equal("INVALID_SRI_ENVIRONMENT", (await Assert.ThrowsAsync<InvalidOperationException>(() => signing.SignDraftAsync(note.Id))).Message);
            Assert.Null((await db.CreditNotes.AsNoTracking().SingleAsync()).SriSignedXml); return;
        }
        await signing.SignDraftAsync(note.Id);
        var signed = (await db.CreditNotes.AsNoTracking().SingleAsync()).SriSignedXml;
        Assert.NotNull(signed);
        if (scenario == "revoked")
        {
            p.Manifest.Crls[1] = p.Crl(p.Intermediate, p.Leaf);
            guard = new FiscalExecutionGuard(db, provider, new FiscalCertificateEvaluator(p.Registry()), f.PublicChains);
        }
        if (scenario == "corrupt-xml")
        {
            var row = await db.CreditNotes.SingleAsync(); row.SriSignatureHash = "00"; await db.SaveChangesAsync();
        }
        var submission = new SriCreditNoteSubmissionService(db, accessor, f.Transport, notes, new SriCreditNoteXmlValidator(),
            clock, f.Options, NullLogger<SriCreditNoteSubmissionService>.Instance, guard);
        if (scenario == "positive")
        {
            await submission.SubmitSignedAsync(note.Id); Assert.Equal(1, f.Transport.Submissions);
            Assert.Equal(signed, f.Transport.SignedXml);
            var replaySigner = new SriCreditNoteSigningService(db, accessor, null!, null!, new SriCreditNoteXmlValidator(), notes,
                clock, NullLogger<SriCreditNoteSigningService>.Instance);
            Assert.Equal(note.Id, (await replaySigner.SignDraftAsync(note.Id)).Id);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => submission.SubmitSignedAsync(note.Id));
            Assert.Equal(0, f.Transport.Submissions);
            Assert.Empty(await db.SriSubmissionAttempts.Where(a => a.CreditNoteId == note.Id).ToListAsync());
        }
        Assert.Equal(signed, await signing.GetSignedXmlAsync(note.Id));
    }

    private sealed class NeverEvaluate : IFiscalCertificateEvaluator
    {
        public FiscalTrustDecision Evaluate(X509Certificate2 signer, IReadOnlyCollection<X509Certificate2> auxiliary, string ruc, DateTime now)
            => throw new InvalidOperationException("Historical query must not evaluate trust.");
    }
    private sealed class FailingCertificateProvider(string failure) : ISriSigningCertificateProvider
    {
        public Task<SriSigningCertificateMaterial> GetActiveCertificateMaterialAsync()
            => failure == "unexpected-exception"
                ? Task.FromException<SriSigningCertificateMaterial>(new CryptographicException("synthetic-private-detail"))
                : Task.FromException<SriSigningCertificateMaterial>(new InvalidOperationException(
                    failure == "unrecognized-operation" ? "synthetic-private-detail" : failure));
    }
    private sealed class CountingEvaluator(IFiscalCertificateEvaluator inner) : IFiscalCertificateEvaluator
    {
        public int Calls;
        public FiscalTrustDecision Evaluate(X509Certificate2 signer, IReadOnlyCollection<X509Certificate2> auxiliary, string ruc, DateTime now)
        { Calls++; return inner.Evaluate(signer, auxiliary, ruc, now); }
    }
    private sealed class PlatformActor : IPlatformContextAccessor
    { public Task<PlatformContext> GetRequiredContextAsync() => Task.FromResult(new PlatformContext(1, "synthetic", "platform@hfpos.test", 1)); }
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
