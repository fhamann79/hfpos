using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
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
public sealed class ProductEditorTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<TestTenant> Tenant(string key = "editor", int seed = 9535, decimal stock = 0, bool fiscal = true)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, stock);
        await using var db = database.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = await db.Products.SingleAsync(p => p.CompanyId == tenant.CompanyId);
        product.VatCategory = ProductVatCategory.Vat15;
        new ProductCostService(db).InitializeManualCost(product, tenant.UserId, Now);
        var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId);
        foreach (var code in new[] { AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite,
            AppPermissions.FiscalSettingsRead, AppPermissions.FiscalSettingsWrite })
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code)
                ?? new Permission { Code = code, Description = "Synthetic editor permission", IsActive = true, CreatedAt = Now };
            db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
        }
        (await db.Companies.SingleAsync(c => c.Id == tenant.CompanyId)).MatrixAddress = "Synthetic matrix";
        await db.SaveChangesAsync();
        if (fiscal) await FiscalTestData.ConfirmAsync(db, tenant);
        await tx.CommitAsync();
        return tenant;
    }

    private static ProductEditorService Editor(PosDbContext db, TestTenant t)
    {
        var accessor = new StaticOperationalContextAccessor(t.OperationalContext);
        var clock = new FixedBusinessClock(Now); var fiscal = new FixedSriFiscalClock(Now);
        return new(db, accessor, new TenantAdministrationGuard(db), new ProductCostService(db),
            new FiscalProfileService(db, accessor, clock, fiscal), clock, fiscal);
    }

    private static DeclareProductFiscalClassificationDto ServiceDeclaration(long revision = 0, long productRevision = 0)
        => new() { ExpectedRevision = revision, ExpectedProductRevision = productRevision, Kind = FiscalProductKind.Service,
            Activity = FiscalActivity.EligibleService, TaxBasis = FiscalTaxBasis.GeneralRate, Confirmed = true,
            LegalReference = "Synthetic service legal reference", EvidenceReference = "Synthetic service evidence" };

    private async Task<ProductEditorSaveDto> New(TestTenant t, bool complete = true)
    {
        await using var db = database.CreateDbContext();
        return new() { CategoryId = await db.Categories.Where(c => c.CompanyId == t.CompanyId).Select(c => c.Id).SingleAsync(),
            Name = "Synthetic copy service", InternalCode = "SYN-COPY", Price = .10m, Cost = .0123m, MinimumStock = 0,
            VatCategory = ProductVatCategory.Vat15, TracksInventory = !complete,
            Classification = complete ? ServiceDeclaration() : null };
    }

    private static ProductEditorSaveDto Edit(ProductEditorStateDto state) => new()
    {
        ExpectedStateToken = state.StateToken, CategoryId = state.Product.CategoryId, Name = state.Product.Name,
        InternalCode = state.Product.InternalCode, Barcode = state.Product.Barcode, Price = state.Product.Price,
        Cost = state.Product.Cost, MinimumStock = state.Product.MinimumStock, VatCategory = state.Product.VatCategory,
        TracksInventory = state.Product.TracksInventory
    };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Complete_service_creation_sells_without_second_edit_and_without_physical_effects(bool invoice)
    {
        var t = await Tenant(); var request = await New(t); ProductEditorStateDto saved;
        await using (var db = database.CreateDbContext()) saved = await Editor(db, t).SaveAsync(null, request);
        Assert.False(saved.Product.TracksInventory); Assert.Empty(saved.SalePending);
        Assert.True(saved.Classification!.IsCurrent); Assert.Equal(t.UserId, saved.Classification.DeclaredByUserId);
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0 });
        var sale = new SaleCreateDto { RequestId = Guid.NewGuid(), CashReceived = 1,
            DocumentType = invoice ? SaleDocumentType.Invoice : SaleDocumentType.Ticket,
            Items = [new() { ProductId = saved.Product.Id, UnitPrice = .10m, Quantity = 1 }] };
        var sales = invoice ? new SalesService(scope.DbContext, NullLogger<SalesService>.Instance,
            new StaticOperationalContextAccessor(t.OperationalContext), scope.Inventory, scope.CashSessions,
            new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, new FixedSriFiscalClock(Now)),
            new SriAccessKeyService(), new SriXmlDraftService(), new FixedSriFiscalClock(Now), new FixedBusinessClock(Now),
            new SriInvoiceXmlValidator(), Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }),
            new TenantAdministrationGuard(scope.DbContext)) : scope.Sales;
        var result = await sales.CreateWithPreviewAsync(sale);
        Assert.Equal(result.Id, (await sales.CreateAsync(sale)).Id);
        Assert.False(await scope.DbContext.ProductStocks.AnyAsync(s => s.ProductId == saved.Product.Id));
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        Assert.False((await scope.DbContext.SaleItems.SingleAsync()).TracksInventorySnapshot);
        Assert.Single(await scope.DbContext.ProductCostEvents.Where(e => e.ProductId == saved.Product.Id).ToListAsync());
    }

    [Fact]
    public async Task Basic_save_keeps_null_identifiers_and_pending_fiscal_rules_also_block_ticket_before_effects()
    {
        var t = await Tenant(fiscal: false); var request = await New(t, false);
        request.InternalCode = null; request.Barcode = null;
        ProductEditorStateDto saved;
        await using (var db = database.CreateDbContext()) saved = await Editor(db, t).SaveAsync(null, request);
        Assert.Null(saved.Classification); Assert.Null(saved.Product.InternalCode); Assert.Null(saved.Product.Barcode);
        Assert.Contains("FISCAL_PROFILE_REQUIRED", saved.SalePending); Assert.Contains("INSUFFICIENT_STOCK", saved.SalePending);
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        var sale = new SaleCreateDto { RequestId = Guid.NewGuid(), DocumentType = SaleDocumentType.Ticket,
            Items = [new() { ProductId = saved.Product.Id, Quantity = 1, UnitPrice = 1 }] };
        Assert.Equal("FISCAL_PROFILE_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.PreviewAsync(sale))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Basic_goods_can_be_completed_on_same_record_and_stock_policy_is_unchanged()
    {
        var t = await Tenant(); var request = await New(t, false);
        ProductEditorStateDto saved;
        await using (var db = database.CreateDbContext()) saved = await Editor(db, t).SaveAsync(null, request);
        var update = Edit(saved); update.Classification = ServiceDeclaration();
        update.Classification.Kind = FiscalProductKind.Goods; update.Classification.Activity = FiscalActivity.EligibleCommerce;
        await using var completing = database.CreateDbContext();
        var complete = await Editor(completing, t).SaveAsync(saved.Product.Id, update);
        Assert.Equal(saved.Product.Id, complete.Product.Id); Assert.True(complete.Product.TracksInventory);
        Assert.Equal(new[] { "INSUFFICIENT_STOCK" }, complete.SalePending);
        Assert.Equal(2, await completing.Products.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Invalid_classification_or_failure_after_flush_rolls_back_product_classification_and_cost(bool afterFlush)
    {
        var t = await Tenant(); var request = await New(t);
        if (!afterFlush) request.Classification!.EvidenceReference = " ";
        await using (var db = database.CreateDbContext(afterFlush ? [new FailAfterSave(2)] : []))
            await Assert.ThrowsAnyAsync<Exception>(() => Editor(db, t).SaveAsync(null, request));
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Products.ToListAsync()); Assert.Single(await verify.ProductFiscalClassifications.ToListAsync());
        Assert.Single(await verify.ProductCostEvents.ToListAsync());
        Assert.False(await verify.Products.AnyAsync(p => p.InternalCode == "SYN-COPY"));
    }

    [Fact]
    public async Task Failure_after_update_flush_restores_product_cost_and_classification_history()
    {
        var t = await Tenant(); var id = t.Products[0].Id; ProductEditorStateDto state;
        await using (var read = database.CreateDbContext()) state = await Editor(read, t).GetAsync(id);
        var update = Edit(state); update.Name = "Synthetic failed update"; update.Cost = 5; update.TracksInventory = false;
        update.Classification = ServiceDeclaration(state.ClassificationRevision, state.ProductRevision);
        await using (var write = database.CreateDbContext(new FailAfterSave(1)))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Editor(write, t).SaveAsync(id, update));
        await using var verify = database.CreateDbContext();
        var current = await Editor(verify, t).GetAsync(id);
        Assert.Equal(state.StateToken, current.StateToken); Assert.True(current.Product.TracksInventory);
        Assert.Single(await verify.ProductCostEvents.ToListAsync()); Assert.Single(await verify.ProductFiscalClassifications.ToListAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Explicit_stale_declaration_revisions_fail_even_with_current_editor_token(bool productRevision)
    {
        var t = await Tenant(); var id = t.Products[0].Id;
        await using var db = database.CreateDbContext(); var state = await Editor(db, t).GetAsync(id);
        var update = Edit(state); update.Classification = ServiceDeclaration(state.ClassificationRevision, state.ProductRevision);
        if (productRevision) update.Classification.ExpectedProductRevision++;
        else update.Classification.ExpectedRevision++;
        Assert.Equal("PRODUCT_FISCAL_REVISION_CONFLICT", (await Assert.ThrowsAsync<OperationalContextException>(
            () => Editor(db, t).SaveAsync(id, update))).ErrorCode);
        Assert.Single(await db.ProductCostEvents.ToListAsync()); Assert.Single(await db.ProductFiscalClassifications.ToListAsync());
    }

    [Fact]
    public async Task Rename_and_vat_and_declaration_use_previous_revisions_but_final_fiscal_identity()
    {
        var t = await Tenant(); var id = t.Products[0].Id;
        ProductEditorStateDto state;
        await using (var read = database.CreateDbContext()) state = await Editor(read, t).GetAsync(id);
        var update = Edit(state); update.Name = "Synthetic natural food"; update.VatCategory = ProductVatCategory.Vat0;
        update.Classification = new() { ExpectedRevision = state.ClassificationRevision, ExpectedProductRevision = state.ProductRevision,
            Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce, TaxBasis = FiscalTaxBasis.NaturalAgriculturalFoodArt55_1,
            NaturalAgriculturalFoodConfirmed = true, FoodProcessing = NaturalFoodProcessing.None, Confirmed = true,
            LegalReference = "Synthetic art 55.1", EvidenceReference = "Synthetic food evidence" };
        await using (var write = database.CreateDbContext())
        {
            var saved = await Editor(write, t).SaveAsync(id, update);
            Assert.Equal(state.ProductRevision + 1, saved.ProductRevision); Assert.True(saved.Classification!.IsCurrent);
            Assert.Equal(ProductVatCategory.Vat0, saved.Classification.DeclaredVatCategory);
        }
        await using var verify = database.CreateDbContext();
        var history = await verify.ProductFiscalClassifications.OrderBy(c => c.Revision).ToListAsync();
        Assert.Equal(2, history.Count); Assert.Equal(ProductVatCategory.Vat15, history[0].DeclaredVatCategory);
        Assert.Equal(0, history[0].ProductFiscalRevision); Assert.Equal(1, history[1].ProductFiscalRevision);
    }

    [Theory]
    [InlineData(-1)] [InlineData(1)]
    public async Task Nonzero_stock_in_either_direction_blocks_composite_mode_change_and_all_other_edits(int stock)
    {
        var t = await Tenant(stock: stock); var id = t.Products[0].Id;
        await using var db = database.CreateDbContext(); var state = await Editor(db, t).GetAsync(id);
        var update = Edit(state); update.Name = "Synthetic rename"; update.Cost = 5; update.TracksInventory = false;
        update.Classification = ServiceDeclaration(state.ClassificationRevision, state.ProductRevision);
        var ex = await Assert.ThrowsAsync<OperationalContextException>(() => Editor(db, t).SaveAsync(id, update));
        Assert.Equal("PRODUCT_INVENTORY_MODE_HAS_STOCK", ex.ErrorCode);
        await using var verify = database.CreateDbContext(); var product = await verify.Products.SingleAsync();
        Assert.Equal(state.Product.Name, product.Name); Assert.Equal(state.Product.Cost, product.Cost);
        Assert.True(product.TracksInventory); Assert.Single(await verify.ProductFiscalClassifications.ToListAsync());
        Assert.Single(await verify.ProductCostEvents.ToListAsync());
    }

    [Fact]
    public async Task New_classification_is_used_instead_of_previous_goods_when_disabling_inventory()
    {
        var t = await Tenant(); var id = t.Products[0].Id;
        await using var db = database.CreateDbContext(); var state = await Editor(db, t).GetAsync(id);
        var update = Edit(state); update.Name = "Synthetic new service"; update.TracksInventory = false;
        update.Classification = ServiceDeclaration(state.ClassificationRevision, state.ProductRevision);
        var saved = await Editor(db, t).SaveAsync(id, update);
        Assert.False(saved.Product.TracksInventory); Assert.True(saved.Classification!.IsCurrent); Assert.Empty(saved.SalePending);
    }

    [Fact]
    public async Task Tenant_ids_categories_and_state_tokens_cannot_cross_companies()
    {
        var owner = await Tenant(); var other = await Tenant("editor-other", 9536);
        await using var db = database.CreateDbContext();
        var service = Editor(db, other);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalContextException>(() => service.GetAsync(owner.Products[0].Id))).StatusCode);
        var request = await New(owner);
        Assert.Equal("CATEGORY_NOT_FOUND", (await Assert.ThrowsAsync<OperationalContextException>(() => service.SaveAsync(null, request))).ErrorCode);
        Assert.Equal(2, await db.Products.CountAsync());
    }

    [Theory]
    [InlineData(AppPermissions.CatalogProductsWrite)] [InlineData(AppPermissions.FiscalSettingsWrite)]
    public async Task Permissions_revoked_while_waiting_for_lock_leave_no_partial_creation(string permission)
    {
        var t = await Tenant(); var request = await New(t);
        await using var owner = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(owner).BeginChangeAsync(t.CompanyId);
        await using var waiting = database.CreateDbContext(); var pending = Editor(waiting, t).SaveAsync(null, request);
        try
        {
            await AssertBlocked(waiting, owner);
            await owner.RolePermissions.Where(p => p.Role.CompanyId == t.CompanyId && p.Permission.Code == permission).ExecuteDeleteAsync();
        }
        finally { await tx.CommitAsync(); }
        var error = await Assert.ThrowsAsync<OperationalContextException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(403, error.StatusCode); Assert.Single(await waiting.Products.ToListAsync());
        Assert.Single(await waiting.ProductCostEvents.ToListAsync());
    }

    [Fact]
    public async Task Catalog_only_permission_can_save_basic_but_cannot_declare_or_read_fiscal_evidence()
    {
        var t = await Tenant();
        await using var db = database.CreateDbContext();
        await db.RolePermissions.Where(p => p.Role.CompanyId == t.CompanyId
            && (p.Permission.Code == AppPermissions.FiscalSettingsWrite || p.Permission.Code == AppPermissions.FiscalSettingsRead)).ExecuteDeleteAsync();
        var request = await New(t);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() => Editor(db, t).SaveAsync(null, request))).StatusCode);
        request.TracksInventory = true; request.Classification = null;
        var basic = await Editor(db, t).SaveAsync(null, request); Assert.Null(basic.Classification);
        Assert.Null((await Editor(db, t).GetAsync(t.Products[0].Id)).Classification);
    }

    [Fact]
    public async Task Session_revoked_while_waiting_for_lock_is_revalidated()
    {
        var t = await Tenant(); var request = await New(t);
        await using var owner = database.CreateDbContext();
        var user = await owner.Users.SingleAsync(); var role = await owner.Roles.SingleAsync();
        t.OperationalContext.UserSessionVersion = user.SessionVersion; t.OperationalContext.RoleAuthorizationVersion = role.AuthorizationVersion;
        await using var tx = await new TenantAdministrationGuard(owner).BeginChangeAsync(t.CompanyId);
        await using var waiting = database.CreateDbContext(); var pending = Editor(waiting, t).SaveAsync(null, request);
        try { await AssertBlocked(waiting, owner); user.SessionVersion++; await owner.SaveChangesAsync(); }
        finally { await tx.CommitAsync(); }
        Assert.Equal("SESSION_STALE", (await Assert.ThrowsAsync<OperationalContextException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)))).ErrorCode);
        Assert.Single(await waiting.Products.ToListAsync());
    }

    [Fact]
    public async Task Two_saves_of_same_snapshot_serialize_and_only_one_can_commit()
    {
        var t = await Tenant(); var id = t.Products[0].Id; ProductEditorStateDto state;
        await using (var read = database.CreateDbContext()) state = await Editor(read, t).GetAsync(id);
        var request = Edit(state); request.Price = 12;
        var reached = new AsyncTestSignal(); var release = new AsyncTestSignal();
        await using var firstDb = database.CreateDbContext(SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, reached),
            SqlCommandGateInterceptor.WaitBefore(sql => sql.Contains("FROM \"Users\""), release));
        await using var secondDb = database.CreateDbContext();
        var first = Editor(firstDb, t).SaveAsync(id, request); Task<ProductEditorStateDto>? second = null;
        try { await reached.WaitAsync(); second = Editor(secondDb, t).SaveAsync(id, request); await AssertBlocked(secondDb, firstDb); }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("PRODUCT_EDITOR_STATE_CONFLICT", (await Assert.ThrowsAsync<OperationalContextException>(() => second!.WaitAsync(TimeSpan.FromSeconds(30)))).ErrorCode);
        await using var verify = database.CreateDbContext(); Assert.Single(await verify.ProductCostEvents.ToListAsync());
        Assert.Equal(12, (await verify.Products.SingleAsync()).Price);
    }

    [Fact]
    public async Task Noop_save_preserves_cost_provenance_and_snapshot()
    {
        var t = await Tenant(); var id = t.Products[0].Id;
        await using var db = database.CreateDbContext(); var state = await Editor(db, t).GetAsync(id);
        var result = await Editor(db, t).SaveAsync(id, Edit(state));
        Assert.Equal(state.StateToken, result.StateToken); Assert.Single(await db.ProductCostEvents.ToListAsync());
    }

    [Theory]
    [InlineData(true, false, 4, 2)] [InlineData(false, false, 4, 2)]
    [InlineData(true, true, 4, 4)] [InlineData(false, true, 4, 4)]
    [InlineData(true, false, 5, 5)]
    public async Task Fiscal_or_noop_save_preserves_purchase_provenance_unless_cost_was_explicitly_edited(
        bool fiscal, bool setManual, int editedCost, int expectedAfterCancel)
    {
        var t = await Tenant(); var id = t.Products[0].Id; int supplierId;
        await using (var setup = database.CreateDbContext())
        {
            var supplier = new Supplier { CompanyId = t.CompanyId, Name = "Synthetic supplier", Identification = "SYN-535",
                IsActive = true, CreatedAt = Now };
            setup.Suppliers.Add(supplier); await setup.SaveChangesAsync(); supplierId = supplier.Id;
        }
        int receiptId;
        await using (var buying = new TestServiceScope(database, t.OperationalContext))
        {
            var receipt = await buying.PurchaseReceiptCreates.CreateAsync(new() { RequestId = Guid.NewGuid(), SupplierId = supplierId,
                Items = [new() { ProductId = id, Quantity = 1, UnitCost = 4 }] });
            receiptId = receipt.Id; Assert.Equal(2, receipt.Items.Single().PreviousProductCost);
        }
        await using (var editing = database.CreateDbContext())
        {
            var state = await Editor(editing, t).GetAsync(id); var request = Edit(state);
            request.SetManualCost = setManual; request.Cost = editedCost;
            if (fiscal)
            {
                request.Classification = ServiceDeclaration(state.ClassificationRevision, state.ProductRevision);
                request.Classification.Kind = FiscalProductKind.Goods; request.Classification.Activity = FiscalActivity.EligibleCommerce;
            }
            await Editor(editing, t).SaveAsync(id, request);
            Assert.Equal(setManual || editedCost != 4 ? 3 : 2, await editing.ProductCostEvents.CountAsync());
        }
        await using var canceling = new TestServiceScope(database, t.OperationalContext);
        var controller = new PurchaseReceiptsController(canceling.DbContext, canceling.Inventory,
            new StaticOperationalContextAccessor(t.OperationalContext), new FixedBusinessClock(Now),
            new TenantAdministrationGuard(canceling.DbContext), new ProductCostService(canceling.DbContext),
            canceling.PurchaseReceipts, canceling.PurchaseReceiptCreates);
        Assert.IsType<OkObjectResult>((await controller.Cancel(receiptId, new() { Reason = "Synthetic correction" })).Result);
        var product = await canceling.DbContext.Products.AsNoTracking().SingleAsync();
        Assert.Equal(expectedAfterCancel, product.Cost);
        var current = await canceling.DbContext.ProductCostEvents.SingleAsync(e => e.Id == product.CurrentCostEventId);
        Assert.Equal(ProductCostSourceType.Manual, current.SourceType);
    }

    [Fact]
    public async Task Duplicate_identified_creates_serialize_and_do_not_add_cost_or_classification_twice()
    {
        var t = await Tenant(); var request = await New(t);
        await using var a = database.CreateDbContext(); await using var b = database.CreateDbContext();
        async Task<string> Save(PosDbContext db)
        {
            try { await Editor(db, t).SaveAsync(null, request); return "CREATED"; }
            catch (OperationalContextException ex) { return ex.ErrorCode; }
        }
        var results = await Task.WhenAll(Save(a), Save(b));
        Assert.Contains("CREATED", results); Assert.Contains("PRODUCT_INTERNAL_CODE_ALREADY_EXISTS", results);
        await using var verify = database.CreateDbContext();
        Assert.Equal(2, await verify.Products.CountAsync()); Assert.Equal(2, await verify.ProductCostEvents.CountAsync());
        Assert.Equal(2, await verify.ProductFiscalClassifications.CountAsync());
    }

    private sealed class FailAfterSave(int nth) : SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (++calls == nth) throw new InvalidOperationException("SYNTHETIC_FLUSH_FAILURE");
            return ValueTask.FromResult(result);
        }
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
