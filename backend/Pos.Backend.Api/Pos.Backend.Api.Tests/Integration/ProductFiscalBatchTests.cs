using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ProductFiscalBatchTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Permissions = [AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite,
        AppPermissions.FiscalSettingsRead, AppPermissions.FiscalSettingsWrite, AppPermissions.OpStructureRead];
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Filters_select_explicit_ids_and_preserve_commercial_fields_and_unselected_products()
    {
        var tenant = await TenantAsync("batch", 15501, true, 5m, 0m, 5m);
        var ids = tenant.Products.Select(p => p.Id).ToArray();
        await using var db = database.CreateDbContext();
        var second = await db.Products.SingleAsync(p => p.Id == ids[1]);
        second.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = Service(db, tenant);
        var a = await service.CandidatesAsync("batch-P", null, second.CategoryId, "pending", ProductVatCategory.Vat0);
        var b = await service.CandidatesAsync("batch-P", false, second.CategoryId, "pending", ProductVatCategory.Vat0);
        Assert.Equal(3, a.TotalItems);
        Assert.Equal(ids, a.Items.Select(p => p.Id));
        Assert.Equal(ids[1], Assert.Single(b.Items).Id);
        Assert.Empty((await service.CandidatesAsync("batch-P", false, second.CategoryId, "pending", ProductVatCategory.Vat15)).Items);
        Assert.Empty((await service.CandidatesAsync("absent", false, second.CategoryId, "pending", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("batch-P", false, -1, "pending", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("batch-P", false, second.CategoryId, "current", ProductVatCategory.Vat0)).Items);
        Assert.Equal("FISCAL_BATCH_FILTER_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.CandidatesAsync(null, null, null, null, (ProductVatCategory)999))).ErrorCode);

        var preview = await service.PreviewAsync(Request([ids[1], ids[0]]));
        Assert.Equal(2, preview.Changed);
        Assert.Equal(ids.Take(2), preview.Rows.Select(row => row.ProductId));
        Assert.All(preview.Rows, row =>
        {
            Assert.Equal(ProductVatCategory.Vat0, row.CurrentVat);
            Assert.Equal(ProductVatCategory.Vat15, row.ProposedVat);
            Assert.Equal(.15m, row.ProposedDecision!.Rate);
        });
        Assert.Contains("PRODUCT_INACTIVE", preview.Rows[1].AvailabilityWarnings);
        var applied = await service.ConfirmAsync(Confirmation(preview));
        Assert.Equal(JsonSerializer.Serialize(preview.Rows), JsonSerializer.Serialize(applied.Rows));
        Assert.Equal(applied.OperationId, (await service.ReceiptAsync(applied.OperationId)).OperationId);
        var recent = Assert.Single(await service.RecentReceiptsAsync());
        Assert.Equal(new ProductFiscalBatchReceiptSummary(applied.OperationId, applied.AppliedAt, 2), recent);
        Assert.Equal(2, (await service.CandidatesAsync(null, null, null, "current", ProductVatCategory.Vat15)).TotalItems);
        Assert.Equal(ids[2], Assert.Single((await service.CandidatesAsync(null, null, null, "pending", ProductVatCategory.Vat0)).Items).Id);

        var products = await db.Products.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync();
        Assert.Equal(new[] { ProductVatCategory.Vat15, ProductVatCategory.Vat15, ProductVatCategory.Vat0 },
            products.Select(p => p.VatCategory));
        Assert.Equal(new long[] { 1, 1, 0 }, products.Select(p => p.FiscalRevision));
        Assert.False(products[1].IsActive);
        Assert.All(products, p => { Assert.Equal(10m, p.Price); Assert.Equal(2m, p.Cost); Assert.True(p.TracksInventory); });
        Assert.Equal(2, await db.ProductFiscalClassifications.CountAsync());
        var batch = Assert.Single(await db.InitialDataBatches.ToListAsync());
        Assert.Equal(tenant.UserId, batch.UserId);
        Assert.Equal("Synthetic reason", applied.Reason);
        Assert.Equal(JsonSerializer.Serialize(applied), batch.ResultJson);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var imports = new InitialDataService(scope.DbContext, new StaticOperationalContextAccessor(tenant.OperationalContext),
            new TenantAdministrationGuard(scope.DbContext), scope.Inventory, new ProductCostService(scope.DbContext), protection);
        Assert.Empty(await imports.GetBatchesAsync());
    }

    [Fact]
    public async Task Candidates_return_all_250_or_exact_count_without_rows_and_exclude_other_tenants()
    {
        var tenant = await TenantAsync("limit", 15511, true, 0m);
        var other = await TenantAsync("foreign", 15512, true, 0m);
        await using var writer = database.CreateDbContext();
        var seed = await writer.Products.SingleAsync(p => p.CompanyId == tenant.CompanyId);
        var foreign = await writer.Products.SingleAsync(p => p.CompanyId == other.CompanyId);
        var matches = Enumerable.Range(0, 249).Select(_ => Extra(seed)).ToArray();
        writer.Products.AddRange(matches);
        writer.Products.Add(Extra(foreign));
        await writer.SaveChangesAsync();

        // Insert the 251st match after the count but before the list query.
        var insert = new AfterCandidateCount(async () =>
        {
            var concurrent = Extra(seed);
            concurrent.Name = "A concurrent candidate";
            writer.Products.Add(concurrent);
            await writer.SaveChangesAsync();
        });
        await using var reader = database.CreateDbContext(insert);
        var service = Service(reader, tenant);
        var allowed = await service.CandidatesAsync(null, null, null, null, ProductVatCategory.Vat0);
        Assert.True(insert.Ran);
        Assert.Equal(250, allowed.MaxProducts);
        Assert.Equal(250, allowed.TotalItems);
        Assert.Equal(matches.Select(p => p.Id).Order().Append(seed.Id), allowed.Items.Select(p => p.Id));
        var overflow = await service.CandidatesAsync(null, null, null, null, ProductVatCategory.Vat0);
        Assert.Equal(251, overflow.TotalItems);
        Assert.Empty(overflow.Items);

        static Product Extra(Product template) => new()
        {
            CompanyId = template.CompanyId, CategoryId = template.CategoryId, Name = "Bulk candidate",
            VatCategory = ProductVatCategory.Vat0, IsActive = true, CreatedAt = Now
        };
    }

    [Fact]
    public async Task Recent_receipts_are_bounded_stably_ordered_and_filter_every_scope_dimension()
    {
        var tenant = await TenantAsync("recent", 15513, true, 0m);
        var other = await TenantAsync("other", 15514, true, 0m);
        var unauthorized = await TenantAsync("read-denied", 15515, false, 0m);
        await using var db = database.CreateDbContext();
        var own = Enumerable.Range(0, 22).Select(i => Batch(tenant.OperationalContext, i + 1)).ToArray();
        own[0].CreatedAt = Now.AddMinutes(1);
        var actor = tenant.OperationalContext;
        var hidden = new[]
        {
            Batch(actor, 1, companyId: other.CompanyId),
            Batch(actor, 1, userId: other.UserId),
            Batch(actor, 1, establishmentId: other.OperationalContext.EstablishmentId),
            Batch(actor, 1, emissionPointId: other.OperationalContext.EmissionPointId),
            Batch(actor, 1, "catalog")
        };
        foreach (var batch in hidden) batch.CreatedAt = Now.AddDays(1);
        db.InitialDataBatches.AddRange(own);
        db.InitialDataBatches.AddRange(hidden);
        await db.SaveChangesAsync();
        var service = Service(db, tenant);
        var recent = await service.RecentReceiptsAsync();
        Assert.Equal(own.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).Take(20)
            .Select(b => new ProductFiscalBatchReceiptSummary(b.RequestId, b.CreatedAt, b.RowCount)), recent);
        foreach (var batch in hidden)
            Assert.Equal(404, (await Assert.ThrowsAsync<OperationalContextException>(() => service.ReceiptAsync(batch.RequestId))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() => Service(db, unauthorized).RecentReceiptsAsync())).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(db, unauthorized).CandidatesAsync(null, null, null, null, null))).StatusCode);

        static InitialDataBatch Batch(OperationalContext scope, int rows, string kind = "product-fiscal-batch",
            int? companyId = null, int? userId = null, int? establishmentId = null, int? emissionPointId = null) => new()
        {
            CompanyId = companyId ?? scope.CompanyId, UserId = userId ?? scope.UserId,
            EstablishmentId = establishmentId ?? scope.EstablishmentId, EmissionPointId = emissionPointId ?? scope.EmissionPointId,
            RequestId = Guid.NewGuid(), Kind = kind,
            CreatedAt = Now, RowCount = rows, ResultJson = "not read by recent"
        };
    }

    [Fact]
    public async Task Popular_business_ordinary_goods_match_nominal_category_not_effective_vat()
    {
        var tenant = await TenantAsync("popular", 15516, true, 0m, 0m, 0m);
        var id = tenant.Products[0].Id;
        await using var db = database.CreateDbContext();
        var profile = await db.CompanyFiscalProfiles.SingleAsync();
        profile.Regime = FiscalRegime.PopularBusiness;
        profile.RimpeEligibilityConfirmed = true;
        var exemptId = tenant.Products[1].Id;
        var notSubjectId = tenant.Products[2].Id;
        (await db.Products.SingleAsync(p => p.Id == exemptId)).VatCategory = ProductVatCategory.VatExempt;
        (await db.Products.SingleAsync(p => p.Id == notSubjectId)).VatCategory = ProductVatCategory.VatNotSubject;
        await db.SaveChangesAsync();

        var service = Service(db, tenant);
        var preview = await service.PreviewAsync(Request([id]));
        var row = Assert.Single(preview.Rows);
        Assert.Equal(ProductVatCategory.Vat15, row.ProposedVat);
        Assert.Equal(ProductVatCategory.Vat0, row.ProposedDecision!.Category);
        Assert.Equal(0m, row.ProposedDecision.Rate);
        Assert.Equal(FiscalTaxResolver.RuleVersion, row.ProposedDecision.LegalRuleVersion);
        var applied = await service.ConfirmAsync(Confirmation(preview));
        Assert.Equal(row.ProposedDecision, Assert.Single(applied.Rows).ProposedDecision);
        Assert.Equal(id, Assert.Single((await service.CandidatesAsync(null, null, null, "current", ProductVatCategory.Vat15)).Items).Id);
        Assert.Empty((await service.CandidatesAsync(null, null, null, "current", ProductVatCategory.Vat0)).Items);
        Assert.Equal(exemptId, Assert.Single((await service.CandidatesAsync(null, null, null, null, ProductVatCategory.VatExempt)).Items).Id);
        Assert.Equal(notSubjectId, Assert.Single((await service.CandidatesAsync(null, null, null, null, ProductVatCategory.VatNotSubject)).Items).Id);
    }

    [Fact]
    public async Task Invalid_declarations_and_foreign_ids_reject_entire_batch_and_permissions_are_rechecked()
    {
        var tenant = await TenantAsync("allowed", 15502, true, 0m);
        var commercial = await TenantAsync("commercial", 15503, false, 0m);
        var id = tenant.Products[0].Id;
        var foreign = commercial.Products[0].Id;
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant);
        var unauthorized = Service(db, commercial);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() =>
            unauthorized.PreviewAsync(Request([foreign])))).StatusCode);
        var mixed = await service.PreviewAsync(Request([id, foreign]));
        Assert.Equal(1, mixed.Rejected);
        Assert.Null(mixed.PreviewToken);
        Assert.Null(mixed.Rows[1].Name);
        Assert.Equal("PRODUCT_NOT_FOUND", mixed.Rows[1].Rejection);
        var invalid = Request([id]);
        invalid.Declaration.TaxBasis = FiscalTaxBasis.ConstructionMaterials24_13;
        var rejected = await service.PreviewAsync(invalid);
        Assert.Equal("SRI_MATERIAL_CODE_REQUIRED", Assert.Single(rejected.Rows).Rejection);
        Assert.Null(rejected.PreviewToken);
        Assert.Equal("FISCAL_BATCH_SELECTION_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.PreviewAsync(Request([id, id])))).ErrorCode);

        var valid = await service.PreviewAsync(Request([id]));
        var cross = await TenantAsync("cross", 15504, true, 0m);
        Assert.Equal("FISCAL_BATCH_PREVIEW_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(db, cross).ConfirmAsync(Confirmation(valid)))).ErrorCode);
        var permission = await db.RolePermissions.SingleAsync(r => r.Role.CompanyId == tenant.CompanyId
            && r.Permission.Code == AppPermissions.FiscalSettingsWrite);
        db.RolePermissions.Remove(permission);
        await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.ConfirmAsync(Confirmation(valid)))).StatusCode);
        Assert.Empty(await db.InitialDataBatches.ToListAsync());
        Assert.Empty(await db.ProductFiscalClassifications.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_waits_for_active_state_writer_and_rejects_stale_or_expired_preview()
    {
        var tenant = await TenantAsync("stale", 15505, true, 2m);
        var id = tenant.Products[0].Id;
        await using var writer = database.CreateDbContext();
        var preview = await Service(writer, tenant).PreviewAsync(Request([id]));
        await using var waiter = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(writer).BeginChangeAsync(tenant.CompanyId);
        var confirming = Service(waiter, tenant).ConfirmAsync(Confirmation(preview));
        try
        {
            await AssertBlockedAsync(waiter, writer);
            await writer.Products.Where(p => p.Id == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, false));
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal("FISCAL_BATCH_PREVIEW_CHANGED", (await Assert.ThrowsAsync<OperationalContextException>(() => confirming)).ErrorCode);
        var refreshed = await Service(writer, tenant).PreviewAsync(Request([id]));
        Assert.Equal("FISCAL_BATCH_PREVIEW_EXPIRED", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(waiter, tenant, Now.AddMinutes(15)).ConfirmAsync(Confirmation(refreshed)))).ErrorCode);
        Assert.Empty(await waiter.InitialDataBatches.ToListAsync());
    }

    [Fact]
    public async Task Double_confirm_and_retry_after_expiry_replay_one_receipt_and_legitimate_zero_remains_a_noop()
    {
        var tenant = await TenantAsync("zero", 15506, true, 4m);
        var id = tenant.Products[0].Id;
        await using var setup = database.CreateDbContext();
        await FiscalTestData.ConfirmAsync(setup, tenant);
        var request = ZeroRequest(id);
        var preview = await Service(setup, tenant).PreviewAsync(request);
        Assert.Equal(0, preview.Changed);
        Assert.Equal(1, preview.Unchanged);
        Assert.Equal(0m, Assert.Single(preview.Rows).ProposedDecision!.Rate);
        await using var a = database.CreateDbContext();
        await using var b = database.CreateDbContext();
        var confirmation = Confirmation(preview);
        var results = await Task.WhenAll(Service(a, tenant).ConfirmAsync(confirmation), Service(b, tenant).ConfirmAsync(confirmation));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        var replay = await Service(a, tenant, Now.AddDays(1)).ConfirmAsync(confirmation);
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(replay));
        Assert.Equal("FISCAL_BATCH_REQUEST_CONFLICT", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(a, tenant).ConfirmAsync(confirmation with { PreviewToken = confirmation.PreviewToken + "A" }))).ErrorCode);
        Assert.Single(await setup.InitialDataBatches.ToListAsync());
        Assert.Single(await setup.ProductFiscalClassifications.ToListAsync());
        Assert.Equal(0, (await setup.Products.AsNoTracking().SingleAsync()).FiscalRevision);
    }

    [Fact]
    public async Task Failure_after_flush_rolls_back_both_products_revisions_and_audit_and_same_token_can_retry()
    {
        var tenant = await TenantAsync("rollback", 15507, true, 0m, 0m);
        await using var db = database.CreateDbContext();
        var preview = await Service(db, tenant).PreviewAsync(Request(tenant.Products.Select(p => p.Id).ToArray()));
        await using var failing = database.CreateDbContext(new FailAfterSave());
        await Assert.ThrowsAsync<SyntheticFailure>(() => Service(failing, tenant).ConfirmAsync(Confirmation(preview)));
        Assert.All(await db.Products.ToListAsync(), p => Assert.Equal(ProductVatCategory.Vat0, p.VatCategory));
        Assert.Empty(await db.ProductFiscalClassifications.ToListAsync());
        Assert.Empty(await db.InitialDataBatches.ToListAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(2, (await Service(db, tenant).ConfirmAsync(Confirmation(preview))).Changed);
    }

    [Fact]
    public async Task Legacy_put_cannot_restore_pre_batch_vat_with_stale_revisions_but_non_fiscal_put_still_works()
    {
        var tenant = await TenantAsync("legacy", 15509, true, 2m);
        var id = tenant.Products[0].Id;
        await using var db = database.CreateDbContext();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var product = await db.Products.SingleAsync();
            new ProductCostService(db).InitializeManualCost(product, tenant.UserId, Now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();
        var before = await db.Products.AsNoTracking().SingleAsync();
        var oldEdit = new ProductUpdateDto
        {
            CategoryId = before.CategoryId, Name = before.Name, InternalCode = before.InternalCode,
            Price = before.Price, MinimumStock = before.MinimumStock, Cost = before.Cost,
            VatCategory = before.VatCategory, ExpectedProductFiscalRevision = before.FiscalRevision,
            ExpectedClassificationRevision = 0
        };
        var editor = Service(db, tenant);
        await editor.ConfirmAsync(Confirmation(await editor.PreviewAsync(Request([id]))));

        await using var legacy = database.CreateDbContext();
        var controller = new ProductsController(legacy, new StaticOperationalContextAccessor(tenant.OperationalContext),
            null!, null!, new TenantAdministrationGuard(legacy), new ProductCostService(legacy));
        var conflict = Assert.IsType<ConflictObjectResult>(await controller.Update(id, oldEdit));
        Assert.Equal("PRODUCT_FISCAL_REVISION_CONFLICT", Assert.IsType<ApiErrorResponse>(conflict.Value).Error);
        oldEdit.VatCategory = null;
        oldEdit.Price = 11;
        Assert.IsType<NoContentResult>(await controller.Update(id, oldEdit));
        var persisted = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(11m, persisted.Price);
        Assert.Equal(ProductVatCategory.Vat15, persisted.VatCategory);
        Assert.Equal(1, persisted.FiscalRevision);
        Assert.Single(await db.ProductFiscalClassifications.ToListAsync());
    }

    [Fact]
    public async Task Fiscal_date_rollover_and_inventory_mode_incompatibility_cannot_be_confirmed()
    {
        var tenant = await TenantAsync("rollover", 15510, true, 2m);
        var id = tenant.Products[0].Id;
        await using var db = database.CreateDbContext();
        var midnight = new DateTime(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);
        var preview = await Service(db, tenant, midnight.AddMinutes(-1)).PreviewAsync(Request([id]));
        await using (var confirmationDb = database.CreateDbContext())
            Assert.Equal("FISCAL_BATCH_PREVIEW_CHANGED", (await Assert.ThrowsAsync<OperationalContextException>(() =>
                Service(confirmationDb, tenant, midnight).ConfirmAsync(Confirmation(preview)))).ErrorCode);
        await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.TracksInventory, false));
        var goods = await Service(db, tenant).PreviewAsync(Request([id]));
        Assert.Equal("NON_INVENTORY_REQUIRES_CURRENT_SERVICE", Assert.Single(goods.Rows).Rejection);
        Assert.Null(goods.PreviewToken);
        var services = Request([id]);
        services.Declaration.Kind = FiscalProductKind.Service;
        services.Declaration.Activity = FiscalActivity.EligibleService;
        var service = Service(db, tenant);
        await service.ConfirmAsync(Confirmation(await service.PreviewAsync(services)));
        Assert.False((await db.Products.AsNoTracking().SingleAsync()).TracksInventory);
    }

    [Fact]
    public async Task Historical_invoice_and_tax_snapshot_stay_intact_and_checkout_preview_is_invalidated()
    {
        var tenant = await TenantAsync("history", 15508, true, 5m);
        await using var setup = database.CreateDbContext();
        await FiscalTestData.ConfirmAsync(setup, tenant);
        var id = tenant.Products[0].Id;
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var db = scope.DbContext;
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        var clock = new FixedSriFiscalClock(Now);
        var sales = new SalesService(db, NullLogger<SalesService>.Instance, accessor, scope.Inventory, scope.CashSessions,
            new FiscalDocumentNumberService(db, NullLogger<FiscalDocumentNumberService>.Instance, clock),
            new SriAccessKeyService(), new SriXmlDraftService(), clock, new FixedBusinessClock(Now),
            new SriInvoiceXmlValidator(), Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }),
            new TenantAdministrationGuard(db));
        await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0 });
        SaleCreateDto NewSale() => new() { RequestId = Guid.NewGuid(), CashReceived = 20,
            DocumentType = SaleDocumentType.Invoice, Items = [new() { ProductId = id, UnitPrice = 10, Quantity = 1 }] };
        var original = await sales.CreateWithPreviewAsync(NewSale());
        var checkout = NewSale();
        checkout.FiscalFingerprint = (await sales.PreviewAsync(checkout)).Fingerprint;
        var xml = await db.Sales.Where(s => s.Id == original.Id).Select(s => s.SriXmlDraft).SingleAsync();
        Assert.NotNull(xml);
        var batch = Service(setup, tenant);
        await batch.ConfirmAsync(Confirmation(await batch.PreviewAsync(Request([id]))));

        var after = await db.Sales.AsNoTracking().Include(s => s.Items).SingleAsync(s => s.Id == original.Id);
        Assert.Equal(xml, after.SriXmlDraft);
        Assert.Equal(ProductVatCategory.Vat0, Assert.Single(after.Items).VatCategory);
        Assert.Equal(0m, Assert.Single(after.Items).VatRate);
        Assert.Equal("FISCAL_PREVIEW_CHANGED", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sales.CreateAsync(checkout))).Message);
        Assert.Single(await db.Sales.ToListAsync());
    }

    private async Task<TestTenant> TenantAsync(string key, int seed, bool fiscal, params decimal[] stocks)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, stocks);
        await using var db = database.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId);
        foreach (var code in fiscal ? Permissions : Permissions[..2])
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code)
                ?? new Permission { Code = code, Description = "Synthetic permission", IsActive = true, CreatedAt = Now };
            db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
        }
        var company = await db.Companies.SingleAsync(c => c.Id == tenant.CompanyId);
        company.MatrixAddress = "Synthetic matrix";
        db.CompanyFiscalProfiles.Add(new CompanyFiscalProfile
        {
            CompanyId = tenant.CompanyId, Revision = 1, Regime = FiscalRegime.General, TaxpayerKind = TaxpayerKind.Natural,
            EffectiveFrom = new(2024, 4, 1), DeclaredByUserId = tenant.UserId, DeclaredAt = Now,
            EvidenceReference = "Synthetic profile evidence", Reason = "Synthetic profile", Confirmed = true
        });
        await db.SaveChangesAsync();
        return tenant;
    }

    private ProductFiscalBatchService Service(PosDbContext db, TestTenant tenant, DateTime? instant = null)
    {
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        var clock = new FixedBusinessClock(instant ?? Now);
        var fiscal = new FixedSriFiscalClock(clock.UtcNow);
        return new(db, accessor, new TenantAdministrationGuard(db), new ProductCostService(db),
            new FiscalProfileService(db, accessor, clock, fiscal), clock, fiscal, protection);
    }

    private static ProductFiscalBatchRequest Request(int[] ids) => new(ids, new()
    {
        Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce, TaxBasis = FiscalTaxBasis.GeneralRate,
        Confirmed = true, LegalReference = "Synthetic ordinary VAT", EvidenceReference = "Synthetic shared evidence"
    }, "Synthetic reason");

    private static ProductFiscalBatchRequest ZeroRequest(int id) => new([id], new()
    {
        Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce, TaxBasis = FiscalTaxBasis.NaturalAgriculturalFoodArt55_1,
        Confirmed = true, LegalReference = "Synthetic declared facts matching the test VAT category",
        EvidenceReference = "Synthetic fixture, not taxpayer data", NaturalAgriculturalFoodConfirmed = true,
        FoodProcessing = NaturalFoodProcessing.None
    }, "Synthetic no-op");

    private static ProductFiscalBatchConfirmation Confirmation(ProductFiscalBatchPreview preview)
        => new(preview.OperationId, Assert.IsType<string>(preview.PreviewToken), true);

    private async Task AssertBlockedAsync(PosDbContext waiter, PosDbContext owner)
    {
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ownerId = ((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID;
        while (true)
        {
            if (waiter.Database.GetDbConnection() is NpgsqlConnection { State: ConnectionState.Open } connection)
            {
                await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer);
                command.Parameters.AddWithValue("owner", ownerId);
                command.Parameters.AddWithValue("waiter", connection.ProcessID);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            }
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class SyntheticFailure : Exception;

    private sealed class AfterCandidateCount(Func<Task> action) : DbCommandInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Ran && command.CommandText.Contains("count(*)") && command.CommandText.Contains("\"Products\""))
            {
                Ran = true;
                await action();
            }
            return result;
        }
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new SyntheticFailure();
    }
}
