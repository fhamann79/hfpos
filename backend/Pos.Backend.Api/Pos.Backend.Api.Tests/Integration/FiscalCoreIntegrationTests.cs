using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class FiscalCoreIntegrationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<TestTenant> Tenant(string key, FiscalRegime regime = FiscalRegime.General, int products = 1)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, 7153, Enumerable.Repeat(10m, products).ToArray());
        await using var db = database.CreateDbContext();
        (await db.Companies.SingleAsync()).MatrixAddress = "Synthetic matrix";
        foreach (var product in await db.Products.ToListAsync()) product.VatCategory = ProductVatCategory.Vat15;
        var user = await db.Users.SingleAsync();
        var role = await db.Roles.SingleAsync(); role.Code = "FISCAL_TEST_ADMIN";
        var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == "FISCAL_SETTINGS_WRITE");
        if (permission is null) { permission = new Permission { Code = "FISCAL_SETTINGS_WRITE", Description = "Synthetic", IsActive = true, CreatedAt = Now }; db.Permissions.Add(permission); }
        db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
        await db.SaveChangesAsync(); await FiscalTestData.ConfirmAsync(db, tenant, regime);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0m });
        return tenant;
    }
    private static SaleCreateDto Request(TestTenant t, decimal price = 10m) => new()
    {
        RequestId = Guid.NewGuid(), DocumentType = SaleDocumentType.Invoice, CashReceived = 100m,
        Items = [new() { ProductId = t.Products[0].Id, Quantity = 1m, UnitPrice = price }]
    };
    private static SalesService Sales(TestServiceScope scope, TestTenant tenant, ISriFiscalClock? fiscal = null)
    {
        fiscal ??= new FixedSriFiscalClock(Now);
        return new(scope.DbContext, NullLogger<SalesService>.Instance, new StaticOperationalContextAccessor(tenant.OperationalContext),
            scope.Inventory, scope.CashSessions, new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, fiscal),
            new SriAccessKeyService(), new SriXmlDraftService(), fiscal, new FixedBusinessClock(Now), new SriInvoiceXmlValidator(),
            Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }), new TenantAdministrationGuard(scope.DbContext));
    }
    private static FiscalProfileService Profiles(PosDbContext db, TestTenant t) => new(db,
        new StaticOperationalContextAccessor(t.OperationalContext), new FixedBusinessClock(Now), new FixedSriFiscalClock(Now));
    private static DeclareFiscalProfileDto Change(FiscalRegime regime, DateOnly? date = null) => new()
    {
        ExpectedRevision = 1, Regime = regime, TaxpayerKind = TaxpayerKind.Natural,
        EffectiveFrom = date ?? new DateOnly(2026, 9, 18), Confirmed = true, RimpeEligibilityConfirmed = true,
        Reason = "Synthetic declared transition", EvidenceReference = "Synthetic evidence only"
    };

    [Theory]
    [InlineData(FiscalRegime.General, "1.50", "11.50")]
    [InlineData(FiscalRegime.Entrepreneur, "1.50", "11.50")]
    [InlineData(FiscalRegime.PopularBusiness, "0.00", "10.00")]
    public async Task Preview_checkout_and_XML_use_effective_profile_without_catalog_reclassification(FiscalRegime regime, string tax, string total)
    {
        var tenant = await Tenant("matrix", regime);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var service = Sales(scope, tenant); var request = Request(tenant);
        var preview = await service.PreviewAsync(request); request.FiscalFingerprint = preview.Fingerprint;
        var sale = await service.CreateAsync(request);
        var persisted = await scope.DbContext.Sales.AsNoTracking().SingleAsync();
        var xml = XDocument.Parse(persisted.SriXmlDraft!);
        Assert.Equal(total, xml.Descendants("importeTotal").Single().Value);
        Assert.Equal(tax, xml.Descendants("infoFactura").Elements("totalConImpuestos").Elements("totalImpuesto").Elements("valor").Single().Value);
        Assert.Equal(preview.Total, sale.Total); Assert.Equal(preview.Items[0].VatRate, sale.Items[0].VatRate);
        Assert.Equal(preview.Fingerprint, persisted.FiscalFingerprint);
        Assert.Equal(ProductVatCategory.Vat15, (await scope.DbContext.Products.SingleAsync()).VatCategory);
        Assert.NotNull(persisted.FiscalIssuerSnapshotJson); Assert.NotNull((await scope.DbContext.SaleItems.SingleAsync()).FiscalDecisionJson);
        if (regime == FiscalRegime.General) Assert.Empty(xml.Descendants("contribuyenteRimpe"));
        else Assert.Contains(regime == FiscalRegime.PopularBusiness ? "NEGOCIO POPULAR" : "RÉGIMEN RIMPE", xml.Descendants("contribuyenteRimpe").Single().Value);
    }

    [Fact]
    public async Task One_cent_case_uses_backend_line_order_and_same_rounding_in_preview_persistence_XML()
    {
        var tenant = await Tenant("cent", products: 3);
        await using (var db = database.CreateDbContext())
        {
            foreach (var product in await db.Products.OrderBy(p => p.Id).Skip(1).ToListAsync())
            {
                product.VatCategory = ProductVatCategory.Vat0;
                var classification = await db.ProductFiscalClassifications.SingleAsync(c => c.ProductId == product.Id);
                classification.DeclaredVatCategory = product.VatCategory; classification.TaxBasis = FiscalTaxBasis.NaturalAgriculturalFoodArt55_1;
                classification.NaturalAgriculturalFoodConfirmed = true; classification.FoodProcessing = NaturalFoodProcessing.None;
            }
            await db.SaveChangesAsync();
        }
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); request.DiscountAmount = .01m;
        request.Items = tenant.Products.Reverse().Select(p => new SaleItemCreateDto { ProductId = p.Id, Quantity = 1, UnitPrice = .04m }).ToList();
        var service = Sales(scope, tenant); var preview = await service.PreviewAsync(request);
        Assert.Equal(.12m, preview.Total); request.FiscalFingerprint = preview.Fingerprint;
        var sale = await service.CreateAsync(request);
        Assert.Equal(preview.Total, sale.Total); Assert.Equal(.01m, sale.TaxAmount);
        var xml = XDocument.Parse((await scope.DbContext.Sales.SingleAsync()).SriXmlDraft!);
        Assert.Equal("0.12", xml.Descendants("importeTotal").Single().Value);
    }

    [Fact]
    public async Task Discount_residual_never_creates_negative_line_discount_or_base()
    {
        var tenant = await Tenant("discount-residual");
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); request.DiscountAmount = .03m;
        request.Items = Enumerable.Range(0, 5).Select(_ => new SaleItemCreateDto
            { ProductId = tenant.Products[0].Id, Quantity = 1, UnitPrice = .01m }).ToList();
        var service = Sales(scope, tenant); var preview = await service.PreviewAsync(request);
        Assert.Equal(.02m, preview.Total); Assert.Equal(.03m, preview.Items.Sum(i => i.DiscountAmount));
        Assert.All(preview.Items, i => { Assert.InRange(i.DiscountAmount, 0m, .01m); Assert.InRange(i.TaxableSubtotal, 0m, .01m); });
        request.FiscalFingerprint = preview.Fingerprint;
        Assert.Equal(preview.Total, (await service.CreateAsync(request)).Total);
    }

    [Fact]
    public async Task Missing_or_changed_preview_has_zero_effects_and_replay_ignores_later_fiscal_changes()
    {
        var tenant = await Tenant("replay"); await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var service = Sales(scope, tenant); var request = Request(tenant);
        Assert.Equal("FISCAL_PREVIEW_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request))).Message);
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        await Profiles(scope.DbContext, tenant).DeclareAsync(Change(FiscalRegime.PopularBusiness));
        Assert.Equal("FISCAL_PREVIEW_CHANGED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        var sale = await service.CreateAsync(request); var xml = (await scope.DbContext.Sales.SingleAsync()).SriXmlDraft;
        (await scope.DbContext.Products.SingleAsync()).IsActive = false; await scope.DbContext.SaveChangesAsync();
        var cash = await scope.DbContext.CashSessions.SingleAsync();
        await scope.CashSessions.CloseAsync(cash.Id, new() { CountedCashAmount = sale.Total });
        Assert.Equal(sale.Id, (await service.CreateAsync(request)).Id);
        Assert.Equal(xml, (await scope.DbContext.Sales.SingleAsync()).SriXmlDraft);
        Assert.Single(await scope.DbContext.DocumentSequences.ToListAsync());
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(false, true)]
    public async Task Profile_or_classification_and_checkout_serialize_in_both_real_Postgres_orders(bool changeFirst, bool classification)
    {
        var tenant = await Tenant("race"); var request = Request(tenant);
        await using (var preview = new TestServiceScope(database, tenant.OperationalContext))
            request.FiscalFingerprint = (await Sales(preview, tenant).PreviewAsync(request)).Fingerprint;
        var acquired = new AsyncTestSignal(); var release = new AsyncTestSignal();
        IInterceptor[] gates = [SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, acquired),
            SqlCommandGateInterceptor.WaitBefore(sql => sql.Contains(classification && changeFirst
                ? "FROM \"Products\"" : "FROM \"CompanyFiscalProfiles\""), release)];
        await using var selling = new TestServiceScope(database, tenant.OperationalContext, changeFirst ? [] : gates);
        await using var changing = new TestServiceScope(database, tenant.OperationalContext, changeFirst ? gates : []);
        async Task ChangeAsync()
        {
            if (!classification) await Profiles(changing.DbContext, tenant).DeclareAsync(Change(FiscalRegime.PopularBusiness));
            else await Profiles(changing.DbContext, tenant).DeclareProductClassificationAsync(tenant.Products[0].Id, new()
            {
                ExpectedRevision = 1, ExpectedProductRevision = 0, Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce,
                TaxBasis = FiscalTaxBasis.GeneralRate, Confirmed = true, LegalReference = "LRTI 65",
                EvidenceReference = "Synthetic new evidence"
            });
        }
        // Pause either writer after Company lock, before any fiscal read or irreversible effect.
        Task<Exception?> Capture(Func<Task> action) => CaptureException(action);
        var first = Capture(changeFirst ? ChangeAsync : async () => { await Sales(selling, tenant).CreateAsync(request); });
        Task<Exception?>? second = null;
        try
        {
            await acquired.WaitAsync();
            second = Capture(changeFirst ? async () => { await Sales(selling, tenant).CreateAsync(request); } : ChangeAsync);
            var waiter = changeFirst ? selling.DbContext : changing.DbContext;
            var owner = changeFirst ? changing.DbContext : selling.DbContext;
            await AssertBlocked(waiter, owner);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(30));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(30)));
        var result = await second!.WaitAsync(TimeSpan.FromSeconds(30));
        if (changeFirst) Assert.Equal("FISCAL_PREVIEW_CHANGED", result?.Message); else Assert.Null(result);
        await using var verify = database.CreateDbContext();
        Assert.Equal(changeFirst ? 0 : 1, await verify.Sales.CountAsync());
        Assert.Equal(changeFirst ? 0 : 1, await verify.DocumentSequences.CountAsync());
        Assert.Equal(changeFirst ? 10m : 9m, await TestDataBuilder.GetStockAsync(verify, tenant, tenant.Products[0].Id));
    }
    private static async Task<Exception?> CaptureException(Func<Task> action)
    { try { await action(); return null; } catch (Exception ex) { return ex; } }

    [Fact]
    public async Task Independent_conditions_are_normalized_and_used_versions_cannot_be_rewritten()
    {
        var tenant = await Tenant("conditions"); await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var declaration = Change(FiscalRegime.General); declaration.SpecialTaxpayerResolution = " 001AB9 ";
        declaration.WithholdingAgentResolution = "000123"; declaration.IsAccountingRequired = true;
        var profile = await Profiles(scope.DbContext, tenant).DeclareAsync(declaration);
        Assert.Equal("001AB9", profile.SpecialTaxpayerResolution); Assert.Equal("123", profile.WithholdingAgentResolution);
        var service = Sales(scope, tenant); var request = Request(tenant);
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        await service.CreateAsync(request);
        var xml = XDocument.Parse((await scope.DbContext.Sales.SingleAsync()).SriXmlDraft!);
        Assert.Equal("001AB9", xml.Descendants("contribuyenteEspecial").Single().Value);
        Assert.Equal("123", xml.Descendants("agenteRetencion").Single().Value);
        Assert.Equal("SI", xml.Descendants("obligadoContabilidad").Single().Value);
        declaration.ExpectedRevision = 2;
        Assert.Equal("FISCAL_PROFILE_ALREADY_USED_CHANGE_NEXT_DATE", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Profiles(scope.DbContext, tenant).DeclareAsync(declaration))).Message);
        Assert.Equal(2, await scope.DbContext.CompanyFiscalProfiles.CountAsync());
    }

    [Fact]
    public async Task Unconfigured_tenant_cannot_preview_or_consume_any_operational_effect()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "unknown", 7155, 10m);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var service = Sales(scope, tenant);
        Assert.Equal("FISCAL_PROFILE_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PreviewAsync(Request(tenant)))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync()); Assert.Empty(await scope.DbContext.CashSessions.ToListAsync());
    }

    [Fact]
    public async Task Tenant_permissions_and_composite_foreign_keys_protect_declarations_and_documents()
    {
        var tenant = await Tenant("owner");
        var other = await TestDataBuilder.CreateTenantAsync(database, "other-fiscal", 7154, 5m);
        await using var db = database.CreateDbContext();
        Assert.Empty(await Profiles(db, other).GetHistoryAsync());
        Assert.Equal("FISCAL_PERMISSION_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Profiles(db, other).DeclareAsync(Change(FiscalRegime.General)))).Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Profiles(db, other).GetProductClassificationAsync(tenant.Products[0].Id));
        await FiscalTestData.ConfirmAsync(db, other);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); var service = Sales(scope, tenant);
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        await service.CreateAsync(request);
        var sale = await db.Sales.SingleAsync(); var originalProfile = sale.FiscalProfileId;
        sale.FiscalProfileId = (await db.CompanyFiscalProfiles.SingleAsync(p => p.CompanyId == other.CompanyId)).Id;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        db.ChangeTracker.Clear(); Assert.Equal(originalProfile, (await db.Sales.SingleAsync()).FiscalProfileId);
        var profile = Change(FiscalRegime.PopularBusiness);
        var declared = await Profiles(db, tenant).DeclareAsync(profile);
        Assert.Equal(tenant.UserId, declared.DeclaredByUserId); Assert.Equal(2, declared.Revision);
        Assert.Equal("FISCAL_PROFILE_REVISION_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Profiles(db, tenant).DeclareAsync(profile))).Message);
    }

    [Fact]
    public async Task Midnight_invalidates_old_preview_and_numbering_reuses_the_one_authoritative_instant()
    {
        var tenant = await Tenant("midnight");
        var before = new DateTime(2026, 9, 19, 4, 59, 59, DateTimeKind.Utc);
        var after = before.AddSeconds(2);
        await using (var db = database.CreateDbContext())
            await Profiles(db, tenant).DeclareAsync(Change(FiscalRegime.PopularBusiness, new(2026, 9, 19)));
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var clock = new MutableFiscalClock(before); var service = Sales(scope, tenant, clock); var request = Request(tenant);
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        clock.Now = after;
        Assert.Equal("FISCAL_PREVIEW_CHANGED", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request))).Message);
        Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        request.FiscalFingerprint = (await service.PreviewAsync(request)).Fingerprint;
        // A second clock read would cross a date again; checkout may read UtcNow only once.
        clock.IncrementOnRead = true;
        var sale = await service.CreateAsync(request);
        Assert.Equal(after, sale.DocumentIssuedAt);
        Assert.Equal(10m, sale.Total);
        Assert.Equal("19/09/2026", XDocument.Parse((await scope.DbContext.Sales.SingleAsync()).SriXmlDraft!).Descendants("fechaEmision").Single().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Product_declaration_rejects_stale_product_revision_even_without_an_existing_classification(bool existing)
    {
        var tenant = await Tenant("stale-product"); await using var db = database.CreateDbContext();
        if (!existing) { db.ProductFiscalClassifications.RemoveRange(await db.ProductFiscalClassifications.ToListAsync()); await db.SaveChangesAsync(); }
        var service = Profiles(db, tenant);
        var state = await service.GetProductClassificationAsync(tenant.Products[0].Id);
        var declaration = new DeclareProductFiscalClassificationDto
        {
            ExpectedRevision = state.Classification?.Revision ?? 0, ExpectedProductRevision = state.ProductRevision,
            Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce, TaxBasis = FiscalTaxBasis.GeneralRate,
            LegalReference = "LRTI 65", EvidenceReference = "Synthetic product facts", Confirmed = true
        };
        var product = await db.Products.SingleAsync(); product.Name = "Changed synthetic identity"; product.FiscalRevision++;
        await db.SaveChangesAsync();
        Assert.Equal("PRODUCT_FISCAL_REVISION_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeclareProductClassificationAsync(product.Id, declaration))).Message);
        Assert.Equal(existing ? 1 : 0, await db.ProductFiscalClassifications.CountAsync());
        var fresh = await service.GetProductClassificationAsync(product.Id);
        Assert.Equal(state.ProductRevision + 1, fresh.ProductRevision);
        Assert.Equal(product.Name, fresh.Name); Assert.Equal(product.VatCategory, fresh.VatCategory);
        Assert.Equal(product.InternalCode, fresh.InternalCode); Assert.Equal(product.Barcode, fresh.Barcode);
        declaration.ExpectedProductRevision = fresh.ProductRevision;
        var saved = await service.DeclareProductClassificationAsync(product.Id, declaration);
        Assert.True(saved.IsCurrent); Assert.Equal(declaration.ExpectedRevision + 1, saved.Revision);
    }

    [Fact]
    public async Task Legacy_credit_note_cannot_start_signing_or_reception_but_history_remains_readable()
    {
        var tenant = await Tenant("legacy-nc"); await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); var sales = Sales(scope, tenant);
        request.FiscalFingerprint = (await sales.PreviewAsync(request)).Fingerprint;
        var sale = await sales.CreateAsync(request);
        var original = await scope.DbContext.Sales.SingleAsync(); original.DocumentStatus = SaleDocumentStatus.Authorized;
        original.AuthorizationNumber = original.AccessKey; original.AuthorizedAt = Now; await scope.DbContext.SaveChangesAsync();
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext); var clock = new FixedBusinessClock(Now);
        var options = Options.Create(new SriOptions { Environment = 1, EmissionType = 1 });
        var notes = new CreditNoteService(scope.DbContext, accessor,
            new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, new FixedSriFiscalClock(Now)), clock,
            new SriAccessKeyService(), new SriCreditNoteXmlDraftService(), new SriCreditNoteXmlValidator(), new FixedSriFiscalClock(Now),
            options, NullLogger<CreditNoteService>.Instance, new TenantAdministrationGuard(scope.DbContext));
        var created = await notes.CreateDraftAsync(new() { OriginalSaleId = sale.Id, Reason = "Synthetic return",
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1 }] });
        await notes.PrepareSriDraftAsync(created.Id);
        var note = await scope.DbContext.CreditNotes.SingleAsync(); var xml = note.SriXmlDraft;
        note.FiscalProfileId = null; note.FiscalIssuerSnapshotJson = null; await scope.DbContext.SaveChangesAsync();
        var signing = new SriCreditNoteSigningService(scope.DbContext, accessor, null!, null!, new SriCreditNoteXmlValidator(), notes,
            clock, NullLogger<SriCreditNoteSigningService>.Instance);
        Assert.Equal("FISCAL_LEGACY_REVIEW_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => signing.SignDraftAsync(note.Id))).Message);
        Assert.Null((await scope.DbContext.CreditNotes.AsNoTracking().SingleAsync()).SriSignedXml);
        // Only a sentinel for admission testing; no cryptographic validation or external call is expected.
        note.SriSignedXml = "<synthetic-signed-sentinel/>"; note.SriSignedAt = Now; note.SriSignatureHash = "synthetic";
        note.SriSigningCertificateThumbprint = "synthetic"; note.SriSigningCertificateSubject = "synthetic"; note.SriSigningCertificateSerialNumber = "synthetic";
        await scope.DbContext.SaveChangesAsync();
        var transport = new SyntheticSriTransport();
        var submission = new SriCreditNoteSubmissionService(scope.DbContext, accessor, transport, notes,
            new SriCreditNoteXmlValidator(), clock, options, NullLogger<SriCreditNoteSubmissionService>.Instance);
        Assert.Equal("FISCAL_LEGACY_REVIEW_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => submission.SubmitSignedAsync(note.Id))).Message);
        Assert.Equal(0, transport.Submissions); Assert.Empty(await scope.DbContext.SriSubmissionAttempts.ToListAsync());
        Assert.Equal(note.SriSignedXml, await signing.GetSignedXmlAsync(note.Id));
        note.DocumentStatus = SaleDocumentStatus.Authorized; note.AuthorizationNumber = note.AccessKey; await scope.DbContext.SaveChangesAsync();
        Assert.Equal(note.Id, (await submission.CheckAuthorizationAsync(note.Id)).Id); Assert.Equal(0, transport.Queries);
        Assert.Equal(xml, (await scope.DbContext.CreditNotes.AsNoTracking().SingleAsync()).SriXmlDraft);
    }

    [Fact]
    public async Task Credit_note_freezes_own_issuer_preserves_original_tax_and_blocks_unproven_regime_transition()
    {
        var tenant = await Tenant("nc"); await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var sales = Sales(scope, tenant); var request = Request(tenant); request.Items[0].Quantity = 2;
        request.FiscalFingerprint = (await sales.PreviewAsync(request)).Fingerprint;
        var sale = await sales.CreateAsync(request);
        var original = await scope.DbContext.Sales.SingleAsync();
        original.DocumentStatus = SaleDocumentStatus.Authorized; original.AuthorizationNumber = original.AccessKey;
        original.AuthorizedAt = Now; await scope.DbContext.SaveChangesAsync();
        CreditNoteService Notes(ISriFiscalClock clock) => new(scope.DbContext, new StaticOperationalContextAccessor(tenant.OperationalContext),
            new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, clock), new FixedBusinessClock(Now),
            new SriAccessKeyService(), new SriCreditNoteXmlDraftService(), new SriCreditNoteXmlValidator(), clock,
            Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }), NullLogger<CreditNoteService>.Instance, new TenantAdministrationGuard(scope.DbContext));
        var dto = new CreateCreditNoteDraftDto { OriginalSaleId = sale.Id, Reason = "Synthetic partial return",
            Items = [new() { SaleItemId = sale.Items[0].Id, Quantity = 1 }] };
        var note = await Notes(new FixedSriFiscalClock(Now)).CreateDraftAsync(dto);
        Assert.Equal(1.5m, note.TaxAmount);
        var name = original.FiscalIssuerSnapshotJson;
        (await scope.DbContext.Companies.SingleAsync()).Name = "Changed live issuer";
        (await scope.DbContext.Products.SingleAsync()).VatCategory = ProductVatCategory.Vat0;
        await scope.DbContext.SaveChangesAsync();
        await Profiles(scope.DbContext, tenant).DeclareAsync(Change(FiscalRegime.PopularBusiness, new(2026, 9, 19)));
        var prepared = await Notes(new FixedSriFiscalClock(Now.AddDays(1))).PrepareSriDraftAsync(note.Id);
        var persisted = await scope.DbContext.CreditNotes.AsNoTracking().SingleAsync();
        var xml = XDocument.Parse(persisted.SriXmlDraft!);
        Assert.Equal("Test company nc", xml.Descendants("razonSocial").Single().Value);
        Assert.Empty(xml.Descendants("contribuyenteRimpe")); Assert.Equal("15", xml.Descendants("tarifa").Single().Value);
        Assert.Equal(1.5m, prepared.TaxAmount);
        var savedXml = persisted.SriXmlDraft;
        await Notes(new FixedSriFiscalClock(Now.AddDays(1))).PrepareSriDraftAsync(note.Id);
        Assert.Equal(savedXml, (await scope.DbContext.CreditNotes.AsNoTracking().SingleAsync()).SriXmlDraft);
        Assert.Equal("CREDIT_NOTE_FISCAL_TRANSITION_UNSUPPORTED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Notes(new FixedSriFiscalClock(Now.AddDays(1))).CreateDraftAsync(dto))).Message);
        Assert.Single(await scope.DbContext.CreditNotes.ToListAsync());
        Assert.Equal(name, (await scope.DbContext.Sales.AsNoTracking().SingleAsync()).FiscalIssuerSnapshotJson);
    }

    [Theory]
    [InlineData("declarations")]
    [InlineData("product-revision")]
    [InlineData("snapshot")]
    public async Task Downgrade_cannot_erase_fiscal_evidence_even_without_profile_foreign_keys(string evidence)
    {
        var tenant = evidence == "declarations" ? await Tenant("downgrade")
            : await TestDataBuilder.CreateTenantAsync(database, "downgrade", 7153, 10m);
        await using var db = database.CreateDbContext();
        if (evidence == "product-revision") (await db.Products.SingleAsync()).FiscalRevision = 1;
        if (evidence == "snapshot") db.Sales.Add(new Sale
        {
            CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId, EmissionPointId = tenant.EmissionPointId,
            UserId = tenant.UserId, Number = "SYNTHETIC-DOWNGRADE", Status = SaleStatus.Completed,
            DocumentType = SaleDocumentType.Ticket, DocumentStatus = SaleDocumentStatus.NotRequired,
            CreatedAt = Now, BusinessDate = new(2026, 9, 18), FiscalIssuerSnapshotJson = "synthetic evidence sentinel"
        });
        await db.SaveChangesAsync();
        var profiles = await db.CompanyFiscalProfiles.CountAsync(); var classifications = await db.ProductFiscalClassifications.CountAsync();
        var migrator = db.GetService<IMigrator>();
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator
                .MigrateAsync("20261005131215_AddAssistedPasswordRecovery"));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal("FISCAL_HISTORY_DOWNGRADE_BLOCKED", error.MessageText);
            Assert.Contains("20261020215533_AddTenantFiscalProfiles", await db.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            // Later additive migrations may already have been reverted before the fiscal guard rejects Down.
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
        }
        Assert.Equal(profiles, await db.CompanyFiscalProfiles.CountAsync());
        Assert.Equal(classifications, await db.ProductFiscalClassifications.CountAsync());
        Assert.Equal(evidence == "product-revision" ? 1 : 0, (await db.Products.SingleAsync()).FiscalRevision);
        if (evidence == "snapshot") Assert.Equal("synthetic evidence sentinel", (await db.Sales.SingleAsync()).FiscalIssuerSnapshotJson);
        Assert.Contains("20261020215533_AddTenantFiscalProfiles", await db.Database.GetAppliedMigrationsAsync());
    }

    private sealed class MutableFiscalClock(DateTime now) : ISriFiscalClock
    {
        public DateTime Now { get; set; } = now;
        public bool IncrementOnRead { get; set; }
        public DateTime UtcNow { get { var value = Now; if (IncrementOnRead) Now = Now.AddDays(1); return value; } }
        public DateOnly GetEcuadorFiscalDate(DateTime instant) => DateOnly.FromDateTime(instant.AddHours(-5));
    }
    private async Task AssertBlocked(PosDbContext waiter, PosDbContext owner)
    {
        await using var observer = new NpgsqlConnection(database.ConnectionString); await observer.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ownerId = ((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID;
        while (true)
        {
            if (waiter.Database.GetDbConnection() is NpgsqlConnection { State: System.Data.ConnectionState.Open } connection)
            {
                await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer);
                command.Parameters.AddWithValue("owner", ownerId); command.Parameters.AddWithValue("waiter", connection.ProcessID);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            }
            await Task.Delay(10, timeout.Token);
        }
    }
}
