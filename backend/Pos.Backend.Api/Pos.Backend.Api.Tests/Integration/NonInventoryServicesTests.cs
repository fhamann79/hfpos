using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
public sealed class NonInventoryServicesTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<TestTenant> Tenant(params decimal[] stock)
    {
        var t = await TestDataBuilder.CreateTenantAsync(database, "noninventory", 9531, stock);
        await using var db = database.CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var p in await db.Products.ToListAsync())
        {
            p.VatCategory = ProductVatCategory.Vat15;
            new ProductCostService(db).InitializeManualCost(p, t.UserId, Now);
        }
        (await db.Companies.SingleAsync()).MatrixAddress = "Synthetic matrix address";
        var role = await db.Roles.SingleAsync(); role.Code = "SYNTHETIC_ADMIN";
        var permission = new Permission { Code = "FISCAL_SETTINGS_WRITE", Description = "Synthetic test", IsActive = true, CreatedAt = Now };
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        await db.SaveChangesAsync();
        await FiscalTestData.ConfirmAsync(db, t);
        await tx.CommitAsync();
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0m });
        return t;
    }

    private static FiscalProfileService Profiles(PosDbContext db, TestTenant t) => new(db,
        new StaticOperationalContextAccessor(t.OperationalContext), new FixedBusinessClock(Now), new FixedSriFiscalClock(Now));

    private async Task ConfirmService(TestTenant t, int id)
    {
        await using var db = database.CreateDbContext();
        var state = await Profiles(db, t).GetProductClassificationAsync(id);
        await Profiles(db, t).DeclareProductClassificationAsync(id, new()
        {
            ExpectedRevision = state.Classification!.Revision, ExpectedProductRevision = state.ProductRevision,
            Kind = FiscalProductKind.Service, Activity = FiscalActivity.EligibleService,
            TaxBasis = FiscalTaxBasis.GeneralRate, Confirmed = true,
            LegalReference = "Synthetic general service declaration", EvidenceReference = "Synthetic evidence only"
        });
    }

    private static ProductsController Products(PosDbContext db, TestTenant t) => new(db,
        new StaticOperationalContextAccessor(t.OperationalContext), null!, null!, new TenantAdministrationGuard(db), new ProductCostService(db));

    private async Task<IActionResult> Mode(TestTenant t, int id, bool? tracking, long? expected = 2)
    {
        await using var db = database.CreateDbContext();
        var p = await db.Products.AsNoTracking().SingleAsync(p => p.Id == id);
        return await Products(db, t).Update(id, new()
        {
            CategoryId = p.CategoryId, Name = p.Name, InternalCode = p.InternalCode, Barcode = p.Barcode,
            Price = p.Price, Cost = p.Cost, MinimumStock = p.MinimumStock, VatCategory = p.VatCategory,
            TracksInventory = tracking, ExpectedClassificationRevision = expected
        });
    }

    private static SaleCreateDto Request(TestTenant t, bool invoice = false) => new()
    {
        RequestId = Guid.NewGuid(), CashReceived = 100m,
        DocumentType = invoice ? SaleDocumentType.Invoice : SaleDocumentType.Ticket,
        Items = t.Products.Select(p => new SaleItemCreateDto { ProductId = p.Id, Quantity = 1, UnitPrice = 10 }).ToList()
    };

    private static SalesService InvoiceSales(TestServiceScope scope, TestTenant t) => new(scope.DbContext,
        NullLogger<SalesService>.Instance, new StaticOperationalContextAccessor(t.OperationalContext), scope.Inventory,
        scope.CashSessions, new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, new FixedSriFiscalClock(Now)),
        new SriAccessKeyService(), new SriXmlDraftService(), new FixedSriFiscalClock(Now), new FixedBusinessClock(Now),
        new SriInvoiceXmlValidator(), Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }), new TenantAdministrationGuard(scope.DbContext));

    private static CreditNoteService Notes(TestServiceScope scope, TestTenant t) => new(scope.DbContext,
        new StaticOperationalContextAccessor(t.OperationalContext),
        new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, new FixedSriFiscalClock(Now)),
        new FixedBusinessClock(Now), new SriAccessKeyService(), new SriCreditNoteXmlDraftService(), new SriCreditNoteXmlValidator(),
        new FixedSriFiscalClock(Now), Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }),
        NullLogger<CreditNoteService>.Instance, new TenantAdministrationGuard(scope.DbContext));

    private static CreditNoteInventoryReturnService Returns(TestServiceScope scope, TestTenant t) => new(scope.DbContext,
        new StaticOperationalContextAccessor(t.OperationalContext), scope.Inventory, Notes(scope, t), new FixedBusinessClock(Now),
        NullLogger<CreditNoteInventoryReturnService>.Instance);

    private async Task<int> AuthorizedNote(TestServiceScope scope, TestTenant t, SaleDto sale)
    {
        var original = await scope.DbContext.Sales.SingleAsync(s => s.Id == sale.Id);
        original.DocumentStatus = SaleDocumentStatus.Authorized; original.AuthorizationNumber = original.AccessKey;
        original.AuthorizedAt = Now; await scope.DbContext.SaveChangesAsync();
        var note = await Notes(scope, t).CreateDraftAsync(new()
        {
            OriginalSaleId = sale.Id, Reason = "Synthetic return",
            Items = sale.Items.Select(i => new CreateCreditNoteDraftItemDto { SaleItemId = i.Id, Quantity = 1 }).ToList()
        });
        await Notes(scope, t).PrepareSriDraftAsync(note.Id);
        var persisted = await scope.DbContext.CreditNotes.SingleAsync();
        persisted.DocumentStatus = SaleDocumentStatus.Authorized; persisted.AuthorizationNumber = persisted.AccessKey;
        persisted.AuthorizedAt = Now; await scope.DbContext.SaveChangesAsync();
        return note.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_zero_stock_sells_and_replays_without_physical_rows_and_XML_remains_fiscal(bool mixed)
    {
        var t = await Tenant(mixed ? [0m, 1m] : [0m]);
        var serviceId = t.Products[0].Id;
        await ConfirmService(t, serviceId); Assert.IsType<NoContentResult>(await Mode(t, serviceId, false));
        await using (var setup = database.CreateDbContext())
            await setup.ProductStocks.Where(s => s.ProductId == serviceId).ExecuteDeleteAsync();
        var request = Request(t, invoice: true);
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        var sales = InvoiceSales(scope, t);
        var sale = await sales.CreateWithPreviewAsync(request);
        Assert.Equal(mixed ? 23m : 11.5m, sale.Total);
        Assert.Equal(sale.Id, (await sales.CreateAsync(request)).Id);
        var lines = await scope.DbContext.SaleItems.AsNoTracking().OrderBy(i => i.ProductId).ToListAsync();
        Assert.False(lines[0].TracksInventorySnapshot); if (mixed) Assert.True(lines[1].TracksInventorySnapshot);
        Assert.False(await scope.DbContext.ProductStocks.AnyAsync(s => s.ProductId == serviceId));
        Assert.Equal(mixed ? 1 : 0, await scope.DbContext.InventoryMovements.CountAsync());
        var xml = XDocument.Parse((await scope.DbContext.Sales.SingleAsync()).SriXmlDraft!);
        Assert.Equal(mixed ? "23.00" : "11.50", xml.Descendants("importeTotal").Single().Value);
        Assert.All(lines, i => Assert.Equal(.15m, i.VatRate));
        Assert.Equal(sale.Total, (await scope.CashSessions.GetCurrentAsync())!.CashSalesAmount);
    }

    [Fact]
    public async Task Physical_zero_and_mixed_insufficient_stock_rollback_every_effect()
    {
        var t = await Tenant(0m, 0m); await ConfirmService(t, t.Products[0].Id);
        Assert.IsType<NoContentResult>(await Mode(t, t.Products[0].Id, false));
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        Assert.Equal("INSUFFICIENT_STOCK", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Sales.CreateWithPreviewAsync(Request(t)))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync());
        Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        Assert.Equal(0m, (await scope.CashSessions.GetCurrentAsync())!.CashSalesAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Void_uses_persisted_snapshot_after_mode_changes_in_both_directions(bool originallyPhysical)
    {
        var t = await Tenant(originallyPhysical ? 1m : 0m); var id = t.Products[0].Id;
        await ConfirmService(t, id);
        if (!originallyPhysical) Assert.IsType<NoContentResult>(await Mode(t, id, false));
        var request = Request(t); int saleId;
        await using (var sell = new TestServiceScope(database, t.OperationalContext))
            saleId = (await sell.Sales.CreateWithPreviewAsync(request)).Id;
        Assert.IsType<NoContentResult>(await Mode(t, id, !originallyPhysical));
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        await scope.Sales.VoidAsync(saleId, new() { Reason = "Synthetic correction" });
        Assert.Equal("SALE_ALREADY_VOIDED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Sales.VoidAsync(saleId, new() { Reason = "Synthetic correction" }))).Message);
        Assert.Equal(originallyPhysical ? 1m : 0m, await TestDataBuilder.GetStockAsync(scope.DbContext, t, id));
        Assert.Equal(originallyPhysical ? 2 : 0, await scope.DbContext.InventoryMovements.CountAsync());
        if (originallyPhysical)
            Assert.Single((await scope.Inventory.GetStocksAsync(null, null, false)).Items);
        Assert.Equal(0m, (await scope.CashSessions.GetCurrentAsync())!.CashSalesAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Credit_note_snapshots_filter_only_physical_lines_and_replay_is_complete(bool mixed)
    {
        var t = await Tenant(mixed ? [0m, 1m] : [0m]); var id = t.Products[0].Id;
        await ConfirmService(t, id); Assert.IsType<NoContentResult>(await Mode(t, id, false));
        int noteId;
        await using (var scope = new TestServiceScope(database, t.OperationalContext))
        {
            var sale = await InvoiceSales(scope, t).CreateWithPreviewAsync(Request(t, true));
            noteId = await AuthorizedNote(scope, t, sale);
        }
        // A later classification/mode must not turn the service into a physical return.
        Assert.IsType<NoContentResult>(await Mode(t, id, true));
        if (mixed)
        {
            await ConfirmService(t, t.Products[1].Id);
            Assert.IsType<NoContentResult>(await Mode(t, t.Products[1].Id, false));
        }
        await using var returning = new TestServiceScope(database, t.OperationalContext);
        var beforeXml = (await returning.DbContext.CreditNotes.AsNoTracking().SingleAsync()).SriXmlDraft;
        if (mixed)
        {
            var result = await Returns(returning, t).ReturnToInventoryAsync(noteId, new());
            Assert.True(result.HasInventoryReturn); Assert.True(result.HasPhysicalItems);
            Assert.True((await Returns(returning, t).ReturnToInventoryAsync(noteId, new())).HasInventoryReturn);
            Assert.Single(await returning.DbContext.InventoryMovements.Where(m => m.SourceType == InventoryMovementSourceType.CreditNoteReturn).ToListAsync());
            Assert.Equal(1m, await TestDataBuilder.GetStockAsync(returning.DbContext, t, t.Products[1].Id));
            await returning.DbContext.InventoryMovements.Where(m => m.SourceType == InventoryMovementSourceType.CreditNoteReturn).ExecuteDeleteAsync();
            Assert.Equal("CREDIT_NOTE_INVENTORY_RETURN_INCONSISTENT", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => Returns(returning, t).ReturnToInventoryAsync(noteId, new()))).Message);
            Assert.Equal(1m, await TestDataBuilder.GetStockAsync(returning.DbContext, t, t.Products[1].Id));
        }
        else
        {
            Assert.Equal("CREDIT_NOTE_NO_PHYSICAL_ITEMS", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => Returns(returning, t).ReturnToInventoryAsync(noteId, new()))).Message);
            Assert.False((await Notes(returning, t).GetByIdAsync(noteId)).HasInventoryReturn);
            Assert.False((await Notes(returning, t).GetByIdAsync(noteId)).HasPhysicalItems);
            Assert.Null((await returning.DbContext.CreditNotes.AsNoTracking().SingleAsync()).InventoryReturnedAt);
        }
        Assert.Equal(0m, await TestDataBuilder.GetStockAsync(returning.DbContext, t, id));
        Assert.Equal(beforeXml, (await returning.DbContext.CreditNotes.AsNoTracking().SingleAsync()).SriXmlDraft);
        Assert.False((await returning.DbContext.CreditNoteItems.AsNoTracking().OrderBy(i => i.ProductId).FirstAsync()).TracksInventorySnapshot);
    }

    [Fact]
    public async Task Catalog_mode_requires_current_confirmed_service_revision_and_preserves_omitted_mode()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id;
        Assert.IsType<ConflictObjectResult>(await Mode(t, id, false, 1));
        await ConfirmService(t, id);
        Assert.IsType<ConflictObjectResult>(await Mode(t, id, false, 1));
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        Assert.IsType<NoContentResult>(await Mode(t, id, null));
        await using var db = database.CreateDbContext();
        var p = await db.Products.SingleAsync(); Assert.False(p.TracksInventory); Assert.Equal(0, p.FiscalRevision);
        var profiles = Profiles(db, t);
        Assert.Equal("NON_INVENTORY_REQUIRES_CURRENT_SERVICE", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => profiles.DeclareProductClassificationAsync(id, new()
            {
                ExpectedRevision = 2, ExpectedProductRevision = 0, Kind = FiscalProductKind.Goods,
                Activity = FiscalActivity.EligibleCommerce, TaxBasis = FiscalTaxBasis.GeneralRate,
                Confirmed = true, LegalReference = "Synthetic goods", EvidenceReference = "Synthetic"
            }))).Message);
        db.ChangeTracker.Clear();
        p = await db.Products.SingleAsync(); p.FiscalRevision++; await db.SaveChangesAsync();
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        Assert.Equal("NON_INVENTORY_REQUIRES_CURRENT_SERVICE", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Sales.PreviewAsync(Request(t)))).Message);
    }

    [Fact]
    public async Task Mode_checks_every_establishment_and_create_cannot_skip_classification()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        await using var db = database.CreateDbContext();
        var other = new Establishment { CompanyId = t.CompanyId, Code = "002", Name = "Synthetic second", Address = "Synthetic", IsActive = true, CreatedAt = Now };
        db.Establishments.Add(other); await db.SaveChangesAsync();
        db.ProductStocks.Add(new() { ProductId = id, CompanyId = t.CompanyId, EstablishmentId = other.Id, Quantity = 2m, UpdatedAt = Now });
        await db.SaveChangesAsync();
        var conflict = Assert.IsType<ConflictObjectResult>(await Mode(t, id, false));
        Assert.Equal("PRODUCT_INVENTORY_MODE_HAS_STOCK", Assert.IsType<ApiErrorResponse>(conflict.Value).Error);
        var result = await Products(db, t).Create(new() { Name = "Synthetic new service", CategoryId = (await db.Products.SingleAsync()).CategoryId, TracksInventory = false });
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(await db.Products.ToListAsync());
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("adjust")]
    [InlineData("opening")]
    [InlineData("void")]
    [InlineData("return")]
    public async Task Nontracking_cannot_admit_inventory_or_forge_historical_sources(string operation)
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            switch (operation)
            {
                case "entry": await scope.Inventory.RegisterEntryAsync(new() { RequestId = Guid.NewGuid(), ProductId = id, Quantity = 1 }); break;
                case "adjust": await scope.Inventory.RegisterAdjustmentAsync(new() { RequestId = Guid.NewGuid(), ProductId = id, Quantity = 1,
                    ExpectedCompanyId = t.CompanyId, ExpectedEstablishmentId = t.EstablishmentId,
                    ExpectedQuantity = 0, ExpectedMovementWatermark = 0 }); break;
                case "opening": await scope.Inventory.RegisterOpeningAsync(id, 1, 999, 2); break;
                case "void": await scope.Inventory.RegisterVoidAsync(id, 1, 999, 999, null); break;
                case "return": await scope.Inventory.RegisterCreditNoteReturnAsync(id, 1, 999, 999, null); break;
            }
        });
        Assert.Equal(operation is "void" or "return" ? "INVENTORY_HISTORICAL_SOURCE_INVALID" : "PRODUCT_DOES_NOT_TRACK_INVENTORY", ex.Message);
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        Assert.Equal(0m, await TestDataBuilder.GetStockAsync(scope.DbContext, t, id));
    }

    [Fact]
    public async Task Preview_changes_with_tracking_but_confirmed_replay_ignores_live_product()
    {
        var t = await Tenant(1m); var id = t.Products[0].Id; await ConfirmService(t, id);
        var request = Request(t);
        await using (var preview = new TestServiceScope(database, t.OperationalContext))
            request.FiscalFingerprint = (await preview.Sales.PreviewAsync(request)).Fingerprint;
        await using (var setup = database.CreateDbContext()) await setup.ProductStocks.ExecuteUpdateAsync(s => s.SetProperty(p => p.Quantity, 0m));
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        Assert.Equal("FISCAL_PREVIEW_CHANGED", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync()); Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        request.FiscalFingerprint = (await scope.Sales.PreviewAsync(request)).Fingerprint;
        var saved = await scope.Sales.CreateAsync(request);
        Assert.IsType<NoContentResult>(await Mode(t, id, true));
        Assert.Equal(saved.Id, (await scope.Sales.CreateAsync(request)).Id);
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Tenant_cannot_sell_or_restore_another_tenants_service_or_physical_history()
    {
        var t = await Tenant(1m);
        await using var owner = new TestServiceScope(database, t.OperationalContext);
        var sale = await owner.Sales.CreateWithPreviewAsync(Request(t));
        var other = await TestDataBuilder.CreateTenantAsync(database, "noninventory-other", 9532, 0m);
        await using var attacker = new TestServiceScope(database, other.OperationalContext);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => attacker.Sales.PreviewAsync(Request(t)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => attacker.Inventory.RegisterVoidAsync(t.Products[0].Id, 1, sale.Id, sale.Items[0].Id, null));
        Assert.IsType<NotFoundObjectResult>(await Mode(other, t.Products[0].Id, false));
        Assert.Equal(0m, await TestDataBuilder.GetStockAsync(owner.DbContext, t, t.Products[0].Id));
    }

    [Fact]
    public async Task Mode_waits_for_operational_share_then_revalidates_the_committed_stock()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        await using var operational = new TestServiceScope(database, t.OperationalContext);
        await using var tx = await operational.DbContext.Database.BeginTransactionAsync();
        await new TenantAdministrationGuard(operational.DbContext).LockOperationalWriteAsync(t.OperationalContext);
        await operational.DbContext.Database.OpenConnectionAsync();
        var ownerPid = ((NpgsqlConnection)operational.DbContext.Database.GetDbConnection()).ProcessID;
        await using var changing = database.CreateDbContext();
        await changing.Database.OpenConnectionAsync();
        var waitingPid = ((NpgsqlConnection)changing.Database.GetDbConnection()).ProcessID;
        var p = await changing.Products.AsNoTracking().SingleAsync();
        var update = Products(changing, t).Update(id, new() { CategoryId = p.CategoryId, Name = p.Name,
            InternalCode = p.InternalCode, Price = p.Price, Cost = p.Cost, VatCategory = p.VatCategory,
            MinimumStock = p.MinimumStock, TracksInventory = false, ExpectedClassificationRevision = 2 });
        try
        {
            await using var observer = new NpgsqlConnection(database.ConnectionString); await observer.OpenAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer);
                command.Parameters.AddWithValue("owner", ownerPid); command.Parameters.AddWithValue("waiter", waitingPid);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) break;
                await Task.Delay(10, timeout.Token);
            }
            Assert.False(update.IsCompleted);
            // Real operational admission within the SHARE transaction; no lock upgrade is needed.
            await operational.Inventory.RegisterPurchaseReceiptAsync(id, 1m, 9001, 9002, "Synthetic operational entry");
            await tx.CommitAsync();
        }
        finally
        {
            if (operational.DbContext.Database.CurrentTransaction is not null) await tx.RollbackAsync();
            await update.WaitAsync(TimeSpan.FromSeconds(30));
        }
        var result = Assert.IsType<ConflictObjectResult>(await update.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("PRODUCT_INVENTORY_MODE_HAS_STOCK", Assert.IsType<ApiErrorResponse>(result.Value).Error);
        await using var verify = database.CreateDbContext();
        Assert.True((await verify.Products.AsNoTracking().SingleAsync()).TracksInventory);
        Assert.Equal(1m, await TestDataBuilder.GetStockAsync(verify, t, id));
    }

    [Fact]
    public async Task Editing_a_nontracking_service_preserves_mode_and_requires_reclassification_before_next_sale()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        await using (var db = database.CreateDbContext())
        {
            var p = await db.Products.AsNoTracking().SingleAsync();
            Assert.IsType<NoContentResult>(await Products(db, t).Update(id, new()
            {
                CategoryId = p.CategoryId, Name = "Synthetic renamed service", Price = p.Price,
                Cost = p.Cost, InternalCode = p.InternalCode, VatCategory = p.VatCategory,
                MinimumStock = p.MinimumStock
            }));
        }
        await using (var invalid = new TestServiceScope(database, t.OperationalContext))
            Assert.Equal("NON_INVENTORY_REQUIRES_CURRENT_SERVICE", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => invalid.Sales.PreviewAsync(Request(t)))).Message);
        await ConfirmService(t, id);
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        await scope.Sales.CreateWithPreviewAsync(Request(t));
        Assert.False((await scope.DbContext.Products.AsNoTracking().SingleAsync()).TracksInventory);
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Purchase_and_transfer_reject_nontracking_before_headers_cost_or_destination_stock()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        int destinationId; int supplierId;
        await using (var db = database.CreateDbContext())
        {
            var destination = new Establishment { CompanyId = t.CompanyId, Code = "002", Name = "Synthetic destination", Address = "Synthetic", IsActive = true, CreatedAt = Now };
            var supplier = new Supplier { CompanyId = t.CompanyId, Name = "Synthetic supplier", Identification = "SYN-SUP", IsActive = true, CreatedAt = Now };
            db.AddRange(destination, supplier); await db.SaveChangesAsync(); destinationId = destination.Id; supplierId = supplier.Id;
        }
        await using var scope = new TestServiceScope(database, t.OperationalContext);
        var before = await scope.DbContext.ProductCostEvents.CountAsync();
        Assert.Equal("PRODUCT_DOES_NOT_TRACK_INVENTORY", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.PurchaseReceiptCreates.CreateAsync(new() { RequestId = Guid.NewGuid(), SupplierId = supplierId,
                Items = [new() { ProductId = id, Quantity = 1, UnitCost = 7 }] }))).Message);
        Assert.Equal("PRODUCT_DOES_NOT_TRACK_INVENTORY", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.Transfers.CreateAsync(new() { RequestId = Guid.NewGuid(), DestinationEstablishmentId = destinationId,
                Items = [new() { ProductId = id, Quantity = 1 }] }))).Message);
        Assert.Empty(await scope.DbContext.PurchaseReceipts.ToListAsync()); Assert.Empty(await scope.DbContext.InventoryTransfers.ToListAsync());
        Assert.False(await scope.DbContext.ProductStocks.AnyAsync(s => s.EstablishmentId == destinationId));
        Assert.Equal(before, await scope.DbContext.ProductCostEvents.CountAsync());
    }

    [Fact]
    public async Task Opening_preview_and_confirm_revalidate_tracking_before_creating_any_stock()
    {
        var t = await Tenant(0m); var id = t.Products[0].Id; await ConfirmService(t, id);
        await using (var db = database.CreateDbContext())
        {
            var role = await db.Roles.SingleAsync();
            foreach (var code in new[] { AppPermissions.InventoryRead, AppPermissions.InventoryWrite, AppPermissions.CatalogProductsRead })
                db.RolePermissions.Add(new() { RoleId = role.Id, Permission = new() { Code = code, Description = "Synthetic", IsActive = true, CreatedAt = Now } });
            await db.SaveChangesAsync();
        }
        var protection = new EphemeralDataProtectionProvider();
        InitialDataService Import(TestServiceScope scope) => new(scope.DbContext, new StaticOperationalContextAccessor(t.OperationalContext),
            new TenantAdministrationGuard(scope.DbContext), scope.Inventory, new ProductCostService(scope.DbContext), protection);
        var request = new InitialDataPreviewRequest { RequestId = Guid.NewGuid(), Kind = "opening-inventory", Csv = "internalCode,quantity\nnoninventory-P1,1\n" };
        string token;
        await using (var preview = new TestServiceScope(database, t.OperationalContext))
        {
            var result = await Import(preview).PreviewAsync(request); Assert.True(result.CanConfirm); token = result.PreviewToken!;
        }
        Assert.IsType<NoContentResult>(await Mode(t, id, false));
        await using var confirm = new TestServiceScope(database, t.OperationalContext);
        var invalid = await Import(confirm).PreviewAsync(request);
        Assert.False(invalid.CanConfirm); Assert.Contains("PRODUCT_DOES_NOT_TRACK_INVENTORY", invalid.Rows.Single().Errors);
        Assert.Equal("INITIAL_DATA_REVALIDATION_FAILED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Import(confirm).ConfirmAsync(new() { Payload = request, PreviewToken = token }))).Message);
        Assert.Empty(await confirm.DbContext.InitialDataBatches.ToListAsync()); Assert.Empty(await confirm.DbContext.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Migration_defaults_legacy_to_physical_and_false_inserts_survive_reload_and_block_down()
    {
        var t = await Tenant(1m);
        await using (var scope = new TestServiceScope(database, t.OperationalContext))
        {
            var sale = await InvoiceSales(scope, t).CreateWithPreviewAsync(Request(t, true));
            await AuthorizedNote(scope, t, sale);
        }
        await using var db = database.CreateDbContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);
        await migrator.MigrateAsync();
        Assert.True((await db.Products.AsNoTracking().SingleAsync()).TracksInventory);
        Assert.True((await db.SaleItems.AsNoTracking().SingleAsync()).TracksInventorySnapshot);
        Assert.True((await db.CreditNoteItems.AsNoTracking().SingleAsync()).TracksInventorySnapshot);
        var original = await db.Products.SingleAsync();
        var inserted = new Product { CompanyId = t.CompanyId, CategoryId = original.CategoryId, Name = "Synthetic false insert", TracksInventory = false, IsActive = true, CreatedAt = Now };
        db.Products.Add(inserted); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.False((await db.Products.AsNoTracking().SingleAsync(p => p.Id == inserted.Id)).TracksInventory);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(migrations[^2]));
        Assert.Contains("DEV531_ROLLBACK_UNSAFE", ex.MessageText);
        Assert.Contains(migrations[^1], await db.Database.GetAppliedMigrationsAsync());
        await db.Products.ExecuteUpdateAsync(p => p.SetProperty(x => x.TracksInventory, true));
        await db.SaleItems.ExecuteUpdateAsync(p => p.SetProperty(x => x.TracksInventorySnapshot, false));
        Assert.Contains("DEV531_ROLLBACK_UNSAFE", (await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(migrations[^2]))).MessageText);
        await db.SaleItems.ExecuteUpdateAsync(p => p.SetProperty(x => x.TracksInventorySnapshot, true));
        await db.CreditNoteItems.ExecuteUpdateAsync(p => p.SetProperty(x => x.TracksInventorySnapshot, false));
        Assert.Contains("DEV531_ROLLBACK_UNSAFE", (await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(migrations[^2]))).MessageText);
    }
}
