using System.Data;
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
public sealed class ProductPriceBatchTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 18, 18, 0, 0, DateTimeKind.Utc);
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Complete_250_selection_exceptions_AND_filters_nominal_VAT_and_exact_overflow_count()
    {
        var tenant = await Tenant("many", 15601, 250);
        var foreign = await Tenant("foreign", 15602);
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant);
        var category = await db.Products.Where(p => p.CompanyId == tenant.CompanyId).Select(p => p.CategoryId).FirstAsync();
        var all = await service.CandidatesAsync("many-P", true, category, "pending", ProductVatCategory.Vat0);
        Assert.Equal(250, all.TotalItems); Assert.Equal(250, all.Items.Length); Assert.Equal(250, all.MaxProducts);
        Assert.DoesNotContain(all.Items, p => p.Id == foreign.Products[0].Id);
        Assert.Empty((await service.CandidatesAsync("many-P", false, category, "pending", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("many-P", true, -1, "pending", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("absent", true, category, "pending", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("many-P", true, category, "current", ProductVatCategory.Vat0)).Items);
        Assert.Empty((await service.CandidatesAsync("many-P", true, category, "pending", ProductVatCategory.Vat15)).Items);

        var excluded = all.Items.Take(3).Select(p => p.Id).ToArray();
        var selected = all.Items.Skip(3).Select(p => p.Id).ToArray();
        var preview = await service.PreviewAsync(Request(selected));
        Assert.Equal(247, preview.Rows.Length); Assert.Equal(247, preview.Changed);
        var result = await service.ConfirmAsync(Confirmation(preview));
        Assert.Equal(247, result.Changed);
        Assert.All(await db.Products.AsNoTracking().Where(p => excluded.Contains(p.Id)).ToArrayAsync(), p => Assert.Equal(10, p.Price));
        Assert.All(await db.Products.AsNoTracking().Where(p => selected.Contains(p.Id)).ToArrayAsync(), p => Assert.Equal(11, p.Price));
        Assert.Equal(10, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == foreign.Products[0].Id)).Price);
        Assert.Equal(250, await db.ProductCostEvents.CountAsync(e => e.CompanyId == tenant.CompanyId));
        Assert.Empty(await db.ProductFiscalClassifications.ToListAsync());

        db.Products.Add(new Product { CompanyId = tenant.CompanyId, CategoryId = category, Name = "extra", InternalCode = "many-P251",
            Price = 10, Cost = 2, IsActive = true, CreatedAt = Now });
        await db.SaveChangesAsync();
        var overflow = await service.CandidatesAsync(null, null, null, null, null);
        Assert.Equal(251, overflow.TotalItems); Assert.Empty(overflow.Items);
        Assert.Equal("PRICE_BATCH_SELECTION_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.PreviewAsync(Request(selected.Concat(excluded).Append(999999).ToArray())))).ErrorCode);
        Assert.Equal("PRICE_BATCH_SELECTION_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.PreviewAsync(Request([selected[0], selected[0]])))).ErrorCode);
    }

    [Fact]
    public async Task Missing_batch_permission_foreign_products_and_zero_cost_margin_cannot_issue_proof()
    {
        var tenant = await Tenant("allowed", 15603);
        var other = await Tenant("other", 15604);
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant);
        var mixed = await service.PreviewAsync(Request([tenant.Products[0].Id, other.Products[0].Id]));
        Assert.Equal(1, mixed.Rejected); Assert.Null(mixed.PreviewToken);
        var rejected = Assert.Single(mixed.Rows.Where(r => r.Outcome == "rejected"));
        Assert.Null(rejected.Name); Assert.Null(rejected.Cost); Assert.Null(rejected.CurrentPrice);
        await db.Products.Where(p => p.Id == tenant.Products[0].Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Cost, 0));
        var zero = await service.PreviewAsync(Request([tenant.Products[0].Id], "gross-margin", 20));
        Assert.Equal("GROSS_MARGIN_ZERO_COST", Assert.Single(zero.Rows).Rejection); Assert.Null(zero.PreviewToken);
        await db.Products.Where(p => p.Id == tenant.Products[0].Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Cost, .0001m));
        var tiny = await service.PreviewAsync(Request([tenant.Products[0].Id], "gross-margin", 20));
        Assert.Equal("GROSS_MARGIN_ROUNDS_TO_ZERO", Assert.Single(tiny.Rows).Rejection); Assert.Null(tiny.PreviewToken);

        var preview = await service.PreviewAsync(Request([tenant.Products[0].Id]));
        var permission = await db.RolePermissions.SingleAsync(r => r.Role.CompanyId == tenant.CompanyId
            && r.Permission.Code == AppPermissions.CatalogProductPricesBatch);
        db.RolePermissions.Remove(permission); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() => service.ConfirmAsync(Confirmation(preview)))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() => service.PreviewAsync(Request([tenant.Products[0].Id])))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<OperationalContextException>(() => service.RecentReceiptsAsync())).StatusCode);
        Assert.Empty(await db.InitialDataBatches.ToListAsync());
    }

    [Theory]
    [InlineData("price")] [InlineData("cost")] [InlineData("cost-revision")]
    [InlineData("cost-event")] [InlineData("name")] [InlineData("classification")]
    public async Task Preview_detects_every_relevant_catalog_state_change(string change)
    {
        var tenant = await Tenant("stale", 15605);
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant); var id = tenant.Products[0].Id;
        var preview = await service.PreviewAsync(Request([id]));
        var product = await db.Products.SingleAsync();
        switch (change)
        {
            case "price": product.Price++; break;
            case "cost": product.Cost++; break;
            case "cost-revision": product.LastCostRevision++; break;
            case "cost-event": product.CurrentCostEventId = null; break;
            case "name": product.Name += " edited"; break;
            case "classification": await FiscalTestData.ConfirmAsync(db, tenant); break;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal("PRICE_BATCH_PREVIEW_CHANGED", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.ConfirmAsync(Confirmation(preview)))).ErrorCode);
        Assert.Empty(await db.InitialDataBatches.ToListAsync());
    }

    [Fact]
    public async Task Double_confirm_response_loss_expiry_and_context_isolation_preserve_one_receipt()
    {
        var tenant = await Tenant("retry", 15606);
        var other = await Tenant("other", 15607);
        await using var setup = database.CreateDbContext();
        var preview = await Service(setup, tenant).PreviewAsync(Request([tenant.Products[0].Id]));
        var confirmation = Confirmation(preview);
        Assert.Equal("PRICE_BATCH_PREVIEW_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(setup, other).ConfirmAsync(confirmation))).ErrorCode);
        var wrong = new OperationalContext { CompanyId = tenant.CompanyId, UserId = tenant.UserId,
            Username = tenant.OperationalContext.Username, EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = other.EmissionPointId };
        // Direct service binding is also enforced, independently of the HTTP context filter.
        Assert.Equal("CONTEXT_MISMATCH", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(setup, tenant, context: wrong).ConfirmAsync(confirmation))).ErrorCode);
        await using var a = database.CreateDbContext(); await using var b = database.CreateDbContext();
        var results = await Task.WhenAll(Service(a, tenant).ConfirmAsync(confirmation), Service(b, tenant).ConfirmAsync(confirmation));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        var replay = await Service(a, tenant, Now.AddDays(1)).ConfirmAsync(confirmation);
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(replay));
        Assert.Equal(11, (await setup.Products.AsNoTracking().SingleAsync(p => p.CompanyId == tenant.CompanyId)).Price);
        Assert.Equal("PRICE_BATCH_REQUEST_CONFLICT", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(a, tenant).ConfirmAsync(confirmation with { PreviewToken = confirmation.PreviewToken + "A" }))).ErrorCode);
        Assert.Single(await setup.InitialDataBatches.ToArrayAsync());
        Assert.Equal(replay.OperationId, Assert.Single(await Service(setup, tenant).RecentReceiptsAsync()).OperationId);
        Assert.Equal(JsonSerializer.Serialize(replay), JsonSerializer.Serialize(await Service(setup, tenant).ReceiptAsync(replay.OperationId)));
        Assert.Empty(await Service(setup, other).RecentReceiptsAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalContextException>(() => Service(setup, other).ReceiptAsync(replay.OperationId))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalContextException>(() => Service(setup, tenant, context: wrong).ReceiptAsync(replay.OperationId))).StatusCode);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var imports = new InitialDataService(scope.DbContext, new StaticOperationalContextAccessor(tenant.OperationalContext),
            new TenantAdministrationGuard(scope.DbContext), scope.Inventory, new ProductCostService(scope.DbContext), protection);
        Assert.Empty(await imports.GetBatchesAsync());
    }

    [Fact]
    public async Task Rollback_after_flush_is_atomic_and_same_proof_retries_then_noop_creates_no_cost_event()
    {
        var tenant = await Tenant("rollback", 15608, 2);
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant);
        var ids = tenant.Products.Select(p => p.Id).ToArray();
        var preview = await service.PreviewAsync(Request(ids));
        await using (var failing = database.CreateDbContext(new FailAfterSave()))
            await Assert.ThrowsAsync<SyntheticFailure>(() => Service(failing, tenant).ConfirmAsync(Confirmation(preview)));
        Assert.All(await db.Products.AsNoTracking().ToArrayAsync(), p => Assert.Equal(10, p.Price));
        Assert.Empty(await db.InitialDataBatches.ToArrayAsync());
        await service.ConfirmAsync(Confirmation(preview));
        var noop = await service.PreviewAsync(Request(ids, "set-price", 11));
        Assert.Equal(2, noop.Unchanged); Assert.Equal(0, noop.Changed);
        await service.ConfirmAsync(Confirmation(noop));
        Assert.Equal(2, await db.ProductCostEvents.CountAsync());
        var expired = await service.PreviewAsync(Request(ids));
        Assert.Equal("PRICE_BATCH_PREVIEW_EXPIRED", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            Service(db, tenant, Now.AddMinutes(15)).ConfirmAsync(Confirmation(expired)))).ErrorCode);
    }

    [Theory]
    [InlineData(11)] [InlineData(10)]
    public async Task Price_or_noop_preserves_receipt_snapshots_stock_and_cost_cancellation_provenance(int price)
    {
        var tenant = await Tenant("receipt", 15609); var id = tenant.Products[0].Id; int supplierId;
        await using (var setup = database.CreateDbContext())
        {
            var supplier = new Supplier { CompanyId = tenant.CompanyId, Name = "Synthetic supplier", Identification = "SYN156",
                IsActive = true, CreatedAt = Now };
            setup.Suppliers.Add(supplier); await setup.SaveChangesAsync(); supplierId = supplier.Id;
        }
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var receipt = await scope.PurchaseReceiptCreates.CreateAsync(new() { RequestId = Guid.NewGuid(), SupplierId = supplierId,
            Items = [new() { ProductId = id, Quantity = 1, UnitCost = 4 }] });
        await using var db = database.CreateDbContext();
        var before = await db.Products.AsNoTracking().SingleAsync();
        var itemBefore = JsonSerializer.Serialize(await db.PurchaseReceiptItems.AsNoTracking().Select(i => new {
            i.Id, i.UnitCost, i.PreviousProductCost, i.AppliedProductCost }).ToArrayAsync());
        var movements = await db.InventoryMovements.CountAsync();
        var stock = await db.ProductStocks.Select(s => s.Quantity).SingleAsync();
        var service = Service(db, tenant);
        await service.ConfirmAsync(Confirmation(await service.PreviewAsync(Request([id], "set-price", price))));
        var after = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(before.Cost, after.Cost); Assert.Equal(before.LastCostRevision, after.LastCostRevision);
        Assert.Equal(before.CurrentCostEventId, after.CurrentCostEventId); Assert.Equal(before.FiscalRevision, after.FiscalRevision);
        Assert.Equal(before.VatCategory, after.VatCategory); Assert.Equal(2, await db.ProductCostEvents.CountAsync());
        Assert.Equal(itemBefore, JsonSerializer.Serialize(await db.PurchaseReceiptItems.AsNoTracking().Select(i => new {
            i.Id, i.UnitCost, i.PreviousProductCost, i.AppliedProductCost }).ToArrayAsync()));
        Assert.Equal(movements, await db.InventoryMovements.CountAsync());
        Assert.Equal(stock, await db.ProductStocks.Select(s => s.Quantity).SingleAsync());
        await using var canceling = new TestServiceScope(database, tenant.OperationalContext);
        var controller = new PurchaseReceiptsController(canceling.DbContext, canceling.Inventory,
            new StaticOperationalContextAccessor(tenant.OperationalContext), new FixedBusinessClock(Now),
            new TenantAdministrationGuard(canceling.DbContext), new ProductCostService(canceling.DbContext),
            canceling.PurchaseReceipts, canceling.PurchaseReceiptCreates);
        Assert.IsType<OkObjectResult>((await controller.Cancel(receipt.Id, new() { Reason = "Synthetic correction" })).Result);
        var canceled = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(2, canceled.Cost); Assert.Equal(price, canceled.Price);
        Assert.Equal(ProductCostSourceType.Manual, (await db.ProductCostEvents.SingleAsync(e => e.Id == canceled.CurrentCostEventId)).SourceType);
    }

    [Fact]
    public async Task Confirmation_waits_for_writer_then_observes_committed_price_change()
    {
        var tenant = await Tenant("locking", 15610);
        await using var writer = database.CreateDbContext(); await using var waiter = database.CreateDbContext();
        var preview = await Service(writer, tenant).PreviewAsync(Request([tenant.Products[0].Id]));
        await using var tx = await new TenantAdministrationGuard(writer).BeginChangeAsync(tenant.CompanyId);
        var confirming = Service(waiter, tenant).ConfirmAsync(Confirmation(preview));
        try
        {
            await using var observer = new NpgsqlConnection(database.ConnectionString); await observer.OpenAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ownerId = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
            while (true)
            {
                if (waiter.Database.GetDbConnection() is NpgsqlConnection { State: ConnectionState.Open } connection)
                {
                    await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer);
                    command.Parameters.AddWithValue("owner", ownerId); command.Parameters.AddWithValue("waiter", connection.ProcessID);
                    if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) break;
                }
                await Task.Delay(10, timeout.Token);
            }
            await writer.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 12));
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal("PRICE_BATCH_PREVIEW_CHANGED", (await Assert.ThrowsAsync<OperationalContextException>(() => confirming)).ErrorCode);
    }

    [Fact]
    public async Task Historical_invoice_XML_tax_and_commercial_snapshots_remain_unchanged()
    {
        var tenant = await Tenant("history", 15611); var id = tenant.Products[0].Id;
        await using var setup = database.CreateDbContext();
        await FiscalTestData.ConfirmAsync(setup, tenant);
        (await setup.Companies.SingleAsync()).MatrixAddress = "Synthetic matrix address";
        (await setup.ProductStocks.SingleAsync()).Quantity = 5;
        await setup.SaveChangesAsync();
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var db = scope.DbContext; var clock = new FixedSriFiscalClock(Now);
        var sales = new SalesService(db, NullLogger<SalesService>.Instance, new StaticOperationalContextAccessor(tenant.OperationalContext),
            scope.Inventory, scope.CashSessions, new FiscalDocumentNumberService(db, NullLogger<FiscalDocumentNumberService>.Instance, clock),
            new SriAccessKeyService(), new SriXmlDraftService(), clock, new FixedBusinessClock(Now), new SriInvoiceXmlValidator(),
            Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }), new TenantAdministrationGuard(db));
        await scope.CashSessions.OpenAsync(new() { OpeningAmount = 0 });
        var original = await sales.CreateWithPreviewAsync(new() { RequestId = Guid.NewGuid(), CashReceived = 20,
            DocumentType = SaleDocumentType.Invoice, Items = [new() { ProductId = id, UnitPrice = 10, Quantity = 1 }] });
        async Task<string> Snapshot() => JsonSerializer.Serialize(await db.Sales.AsNoTracking().Where(s => s.Id == original.Id)
            .Select(s => new { s.Id, s.SriXmlDraft, s.Total, s.Subtotal, Items = s.Items.Select(i => new {
                i.ProductId, i.UnitPrice, i.Quantity, i.VatCategory, i.VatRate, i.TracksInventorySnapshot }).ToArray() }).SingleAsync());
        var before = await Snapshot();
        Assert.NotNull((await db.Sales.AsNoTracking().SingleAsync()).SriXmlDraft);
        var batch = Service(setup, tenant);
        await batch.ConfirmAsync(Confirmation(await batch.PreviewAsync(Request([id]))));
        Assert.Equal(before, await Snapshot());
        Assert.Equal(4, await db.ProductStocks.Select(s => s.Quantity).SingleAsync());
        Assert.Single(await db.ProductCostEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Batch_transport_bound_hides_unsafe_legacy_amounts_and_keeps_rule_and_margins_exact()
    {
        var tenant = await Tenant("transport", 15612); var id = tenant.Products[0].Id;
        await using var db = database.CreateDbContext();
        var service = Service(db, tenant);
        foreach (var (price, cost) in new[] { (90071992547409.93m, 2m), (10m, 1000000000.0001m), (1000000000.01m, 2m) })
        {
            await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, price).SetProperty(p => p.Cost, cost));
            var rejected = await service.PreviewAsync(Request([id], "set-price", 10));
            var row = Assert.Single(rejected.Rows);
            Assert.Equal("PRICE_BATCH_AMOUNT_LIMIT", row.Rejection); Assert.Null(rejected.PreviewToken);
            Assert.Null(row.CurrentPrice); Assert.Null(row.Cost); Assert.Null(row.CurrentMargin); Assert.Null(row.ProposedPrice);
            Assert.DoesNotContain("90071992547409", JsonSerializer.Serialize(rejected));
        }
        await db.Products.ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, .01m).SetProperty(p => p.Cost, 999999999.9999m));
        var preview = await service.PreviewAsync(Request([id], "set-price", 1000000000));
        var reviewed = Assert.Single(preview.Rows);
        Assert.Equal("-9999999999899", reviewed.CurrentMargin);
        Assert.Equal("0", reviewed.ProposedMargin); // Four-decimal realized margin, not a fabricated target.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var transport = JsonSerializer.Serialize(preview, options);
        Assert.Contains("\"currentMargin\":\"-9999999999899\"", transport);
        var result = await service.ConfirmAsync(Confirmation(preview));
        Assert.Equal(transport, JsonSerializer.Serialize(preview, options));
        Assert.Equal(JsonSerializer.Serialize(reviewed), JsonSerializer.Serialize(Assert.Single(result.Rows)));
        Assert.Equal(1000000000m, (await db.Products.AsNoTracking().SingleAsync()).Price);
        Assert.Equal(999999999.9999m, (await db.Products.AsNoTracking().SingleAsync()).Cost);
        var precise = await service.PreviewAsync(Request([id], "decrease-percent", 1.2345m));
        Assert.Equal(1.2345m, precise.Value); Assert.Contains("1.2345", JsonSerializer.Serialize(precise, options));
        Assert.Equal("PRICE_BATCH_ACTION_INVALID", (await Assert.ThrowsAsync<OperationalContextException>(() =>
            service.PreviewAsync(Request([id], "decrease-percent", 1.23451m)))).ErrorCode);
    }

    private async Task<TestTenant> Tenant(string key, int seed, int count = 1)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, new decimal[count]);
        await using var db = database.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId);
        foreach (var code in new[] { AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductPricesBatch, AppPermissions.OpStructureRead })
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code)
                ?? new Permission { Code = code, Description = "Synthetic permission", IsActive = true, CreatedAt = Now };
            db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var product in await db.Products.Where(p => p.CompanyId == tenant.CompanyId).ToArrayAsync())
            new ProductCostService(db).InitializeManualCost(product, tenant.UserId, Now);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return tenant;
    }

    private ProductPriceBatchService Service(PosDbContext db, TestTenant tenant, DateTime? now = null, OperationalContext? context = null)
        => new(db, new StaticOperationalContextAccessor(context ?? tenant.OperationalContext), new TenantAdministrationGuard(db),
            new ProductCostService(db), new FixedBusinessClock(now ?? Now), protection);
    private static ProductPriceBatchRequest Request(int[] ids, string action = "increase-percent", decimal value = 10)
        => new(ids, action, value, "Synthetic price adjustment");
    private static ProductPriceBatchConfirmation Confirmation(ProductPriceBatchPreview preview)
        => new(preview.OperationId, Assert.IsType<string>(preview.PreviewToken), true);
    private sealed class SyntheticFailure : Exception;
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new SyntheticFailure();
    }
}
