using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class CriticalOperationReliabilityTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Operation(TestTenant Tenant, string Kind, Guid RequestId, int SupplierId, int CashId,
        decimal Quantity = 2m, decimal Cost = 4m, string Notes = "Synthetic operation",
        int Watermark = 0, decimal ExpectedQuantity = 10m);

    private async Task<Operation> Prepare(string kind, int seed = 952)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, $"critical-{seed}", seed, 10m, 10m);
        await using var db = database.CreateDbContext();
        var supplier = new Supplier { CompanyId = tenant.CompanyId, Name = "Synthetic supplier",
            IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Suppliers.Add(supplier);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var costs = new ProductCostService(db);
            foreach (var product in await db.Products.Where(p => p.CompanyId == tenant.CompanyId).ToListAsync())
                costs.InitializeManualCost(product, tenant.UserId, DateTime.UtcNow);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var cash = await scope.CashSessions.OpenAsync(new() { OpeningAmount = 5m });
        if (kind == "open") await scope.CashSessions.CloseAsync(cash.Id, new() { CountedCashAmount = 5m });
        return new(tenant, kind, Guid.NewGuid(), supplier.Id, cash.Id);
    }

    private static PurchaseReceiptCreateDto Receipt(Operation op) => new()
    {
        RequestId = op.RequestId, SupplierId = op.SupplierId, Notes = op.Notes,
        ReceiptNumber = " SYNTHETIC-529 ",
        Items = [new() { ProductId = op.Tenant.Products[0].Id, Quantity = op.Quantity, UnitCost = op.Cost }]
    };

    private static async Task<int> Send(TestServiceScope scope, Operation op)
    {
        var product = op.Tenant.Products[0].Id;
        return op.Kind switch
        {
            "receipt" => (await scope.PurchaseReceiptCreates.CreateAsync(Receipt(op))).Id,
            "entry" => (await scope.Inventory.RegisterEntryAsync(new() { RequestId = op.RequestId,
                ProductId = product, Quantity = op.Quantity, Reference = op.Notes, Notes = op.Notes })).Id,
            "exit" => (await scope.Inventory.RegisterExitAsync(new() { RequestId = op.RequestId,
                ProductId = product, Quantity = op.Quantity, Reference = op.Notes, Notes = op.Notes })).Id,
            "adjust" => (await scope.Inventory.RegisterAdjustmentAsync(new() { RequestId = op.RequestId,
                ProductId = product, Quantity = op.Quantity, Reference = op.Notes, Notes = op.Notes,
                ExpectedCompanyId = op.Tenant.CompanyId, ExpectedEstablishmentId = op.Tenant.EstablishmentId,
                ExpectedMovementWatermark = op.Watermark, ExpectedQuantity = op.ExpectedQuantity })).Id,
            "cash-in" or "cash-out" => (await scope.CashSessions.AddMovementAsync(op.CashId, new() {
                RequestId = op.RequestId, Type = op.Kind == "cash-in" ? CashMovementType.CashIn : CashMovementType.CashOut,
                Amount = op.Quantity, Reason = op.Notes })).Movements.Single(m => m.RequestId == op.RequestId).Id,
            "open" => (await scope.CashSessions.OpenAsync(new() { RequestId = op.RequestId,
                OpeningAmount = op.Quantity, OpeningNotes = op.Notes })).Id,
            _ => throw new ArgumentOutOfRangeException(nameof(op))
        };
    }

    [Theory]
    [InlineData("receipt")] [InlineData("entry")] [InlineData("exit")] [InlineData("adjust")]
    [InlineData("cash-in")] [InlineData("cash-out")]
    public async Task Lost_response_replay_uses_original_result_and_material_conflict_is_atomic(string kind)
    {
        var op = await Prepare(kind);
        int id;
        await using (var original = new TestServiceScope(database, op.Tenant.OperationalContext))
            id = await Send(original, op); // Discard the transport response; recover with a fresh scope.
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext, new FixedBusinessClock(DateTime.UtcNow.AddDays(1)));
        Assert.Equal(id, await Send(retry, op with { Notes = "  " + op.Notes + "  ", Quantity = 2.00001m }));
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(retry, op with { Quantity = 3m }));
        Assert.Equal("REQUEST_CONFLICT", conflict.Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(kind == "receipt" ? 1 : 0, await verify.PurchaseReceipts.CountAsync());
        Assert.Equal(kind.StartsWith("cash") ? 0 : 1, await verify.InventoryMovements.CountAsync());
        Assert.Equal(kind.StartsWith("cash") ? 1 : 0, await verify.CashMovements.CountAsync());
        Assert.Equal(kind switch { "receipt" or "entry" => 12m, "exit" => 8m, "adjust" => 2m, _ => 10m },
            await TestDataBuilder.GetStockAsync(verify, op.Tenant, op.Tenant.Products[0].Id));
        Assert.Equal(kind == "receipt" ? 3 : 2, await verify.ProductCostEvents.CountAsync());
        var cash = await retry.CashSessions.GetByIdAsync(op.CashId);
        Assert.Equal(kind switch { "cash-in" => 7m, "cash-out" => 3m, _ => 5m }, cash!.ExpectedCashAmount);
    }

    [Theory]
    [InlineData("receipt", false)] [InlineData("entry", false)] [InlineData("exit", false)]
    [InlineData("adjust", false)] [InlineData("cash-in", false)] [InlineData("cash-out", false)]
    [InlineData("receipt", true)] [InlineData("entry", true)] [InlineData("cash-in", true)]
    public async Task Concurrent_same_key_waits_on_real_company_lock_then_replays_or_conflicts(string kind, bool changed)
    {
        var op = await Prepare(kind);
        var held = new AsyncTestSignal(); var release = new AsyncTestSignal();
        await using var owner = new TestServiceScope(database, op.Tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, held),
            SqlCommandGateInterceptor.WaitBefore(sql => kind.StartsWith("cash")
                ? SqlCommandMatchers.CashSessionByIdForUpdate(sql) : sql.Contains("FROM \"Products\""), release));
        await using var waiter = new TestServiceScope(database, op.Tenant.OperationalContext);
        Task<int>? first = null; Task<int>? second = null;
        try
        {
            first = Send(owner, op); await held.WaitAsync();
            second = Send(waiter, changed ? op with { Quantity = 3m } : op);
            await WaitBlocked(waiter, owner);
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }
        finally { release.Set(); }
        var id = await first!.WaitAsync(TimeSpan.FromSeconds(30));
        if (changed)
            Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
                () => second!.WaitAsync(TimeSpan.FromSeconds(30)))).Message);
        else Assert.Equal(id, await second!.WaitAsync(TimeSpan.FromSeconds(30)));
        await using var verify = database.CreateDbContext();
        Assert.Equal(kind.StartsWith("cash") ? 1 : 0, await verify.CashMovements.CountAsync());
        Assert.Equal(kind.StartsWith("cash") ? 0 : 1, await verify.InventoryMovements.CountAsync());
        Assert.Equal(kind == "receipt" ? 1 : 0, await verify.PurchaseReceipts.CountAsync());
    }

    [Fact]
    public async Task Adjustment_replay_precedes_stale_snapshot_but_new_intent_is_rejected()
    {
        var op = await Prepare("adjust");
        await using var first = new TestServiceScope(database, op.Tenant.OperationalContext);
        var id = await Send(first, op);
        await using var next = new TestServiceScope(database, op.Tenant.OperationalContext);
        await Send(next, op with { Kind = "entry", RequestId = Guid.NewGuid(), Quantity = 1m });
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext);
        var result = await retry.Inventory.GetMovementByIdAsync(id);
        Assert.Equal(10m, result!.StockBefore); Assert.Equal(2m, result.StockAfter);
        Assert.Equal(id, await Send(retry, op));
        Assert.Equal("INVENTORY_SNAPSHOT_STALE", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(retry, op with { RequestId = Guid.NewGuid() }))).Message);
        await using var db = database.CreateDbContext();
        Assert.Equal(3m, await TestDataBuilder.GetStockAsync(db, op.Tenant, op.Tenant.Products[0].Id));
        Assert.Equal(2, await db.InventoryMovements.CountAsync());
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(retry, op with { ExpectedQuantity = 3m }))).Message);
    }

    [Fact]
    public async Task Receipt_line_order_is_material_and_canceled_inactive_replay_never_recreates_effects()
    {
        var op = await Prepare("receipt");
        var request = Receipt(op);
        request.Items.Add(new() { ProductId = op.Tenant.Products[0].Id, Quantity = 1m, UnitCost = 8m });
        await using var original = new TestServiceScope(database, op.Tenant.OperationalContext);
        var receipt = await original.PurchaseReceiptCreates.CreateAsync(request);
        Assert.Equal(new[] { 2m, 4m }, receipt.Items.Select(i => i.PreviousProductCost));
        request.Items.Reverse();
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => original.PurchaseReceiptCreates.CreateAsync(request))).Message);
        request.Items.Reverse();
        var controller = new PurchaseReceiptsController(original.DbContext, original.Inventory,
            new StaticOperationalContextAccessor(op.Tenant.OperationalContext), new FixedBusinessClock(),
            new TenantAdministrationGuard(original.DbContext), new ProductCostService(original.DbContext),
            original.PurchaseReceipts, original.PurchaseReceiptCreates);
        Assert.IsType<OkObjectResult>((await controller.Cancel(receipt.Id, new() { Reason = "Synthetic cancellation" })).Result);
        await using (var db = database.CreateDbContext())
        {
            var product = await db.Products.FindAsync(op.Tenant.Products[0].Id); product!.IsActive = false;
            var supplier = await db.Suppliers.FindAsync(op.SupplierId); supplier!.IsActive = false;
            await db.SaveChangesAsync();
        }
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext);
        var replay = await retry.PurchaseReceiptCreates.CreateAsync(request);
        Assert.Equal(receipt.Id, replay.Id); Assert.Equal(PurchaseReceiptStatus.Canceled, replay.Status);
        Assert.Equal(receipt.Items.Select(i => i.Id), replay.Items.Select(i => i.Id));
        Assert.Equal(receipt.Items.Select(i => i.AppliedProductCost), replay.Items.Select(i => i.AppliedProductCost));
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.PurchaseReceipts.ToListAsync());
        Assert.Equal(4, await verify.InventoryMovements.CountAsync());
        Assert.Equal(10m, await TestDataBuilder.GetStockAsync(verify, op.Tenant, op.Tenant.Products[0].Id));
        Assert.Equal(2m, (await verify.Products.FindAsync(op.Tenant.Products[0].Id))!.Cost);
    }

    [Theory]
    [InlineData("receipt")] [InlineData("entry")] [InlineData("cash-in")]
    public async Task Rollback_discards_stock_cost_receipt_movement_and_tracked_changes(string kind)
    {
        var op = await Prepare(kind);
        var failProduct = kind == "receipt" ? op.Tenant.Products[1].Id : op.Tenant.Products[0].Id;
        await using var scope = new TestServiceScope(database, op.Tenant.OperationalContext, new FailMovement(failProduct, kind == "cash-in"));
        if (kind == "receipt")
        {
            var request = Receipt(op);
            request.Items.Add(new() { ProductId = failProduct, Quantity = 3m, UnitCost = 9m });
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.PurchaseReceiptCreates.CreateAsync(request));
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Send(scope, op));
        Assert.Empty(scope.DbContext.ChangeTracker.Entries());
        await using var db = database.CreateDbContext();
        Assert.Empty(await db.PurchaseReceipts.ToListAsync()); Assert.Empty(await db.InventoryMovements.ToListAsync());
        Assert.Empty(await db.CashMovements.ToListAsync()); Assert.Equal(2, await db.ProductCostEvents.CountAsync());
        foreach (var product in op.Tenant.Products)
        {
            Assert.Equal(10m, await TestDataBuilder.GetStockAsync(db, op.Tenant, product.Id));
            Assert.Equal(2m, (await db.Products.FindAsync(product.Id))!.Cost);
        }
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext);
        Assert.True(await Send(retry, op) > 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Manual_cash_and_close_serialize_in_both_orders_and_committed_replay_survives_close(bool closeFirst)
    {
        var op = await Prepare("cash-in");
        var held = new AsyncTestSignal(); var release = new AsyncTestSignal();
        var gates = new IInterceptor[] { SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, held),
            SqlCommandGateInterceptor.WaitBefore(SqlCommandMatchers.CashSessionByIdForUpdate, release) };
        await using var movement = new TestServiceScope(database, op.Tenant.OperationalContext, closeFirst ? [] : gates);
        await using var close = new TestServiceScope(database, op.Tenant.OperationalContext, closeFirst ? gates : []);
        async Task Close() => await close.CashSessions.CloseAsync(op.CashId, new() { CountedCashAmount = 7m });
        async Task<string> Move() { try { await Send(movement, op); return "OK"; }
            catch (InvalidOperationException e) { return e.Message; } }
        Task? owner = null; Task? waiter = null; Task<string>? move = null;
        try
        {
            if (closeFirst) { owner = Close(); await held.WaitAsync(); move = Move(); waiter = move; }
            else { move = Move(); owner = move; await held.WaitAsync(); waiter = Close(); }
            await WaitBlocked(closeFirst ? movement : close, closeFirst ? close : movement);
            Assert.False(owner.IsCompleted); Assert.False(waiter.IsCompleted);
        }
        finally { release.Set(); }
        await Task.WhenAll(owner!, waiter!).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(closeFirst ? "CASH_SESSION_ALREADY_CLOSED" : "OK", await move!);
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext);
        if (!closeFirst) Assert.True(await Send(retry, op) > 0);
        Assert.Equal("CASH_SESSION_ALREADY_CLOSED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(retry, op with { RequestId = Guid.NewGuid() }))).Message);
        Assert.Equal("CASH_SESSION_ALREADY_CLOSED", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => retry.CashSessions.CloseAsync(op.CashId, new() { CountedCashAmount = 7m }))).Message);
        await using var db = database.CreateDbContext();
        Assert.Equal(closeFirst ? 0 : 1, await db.CashMovements.CountAsync());
        Assert.Equal(CashSessionStatus.Closed, (await db.CashSessions.FindAsync(op.CashId))!.Status);
    }

    [Theory]
    [InlineData("receipt")] [InlineData("entry")] [InlineData("cash-in")]
    [InlineData("open")]
    public async Task Same_key_is_independent_in_another_tenant_and_conflicts_for_another_actor(string kind)
    {
        var op = await Prepare(kind);
        await using var owner = new TestServiceScope(database, op.Tenant.OperationalContext);
        var id = await Send(owner, op);
        var other = await Prepare(kind, 953);
        await using var otherScope = new TestServiceScope(database, other.Tenant.OperationalContext);
        Assert.NotEqual(id, await Send(otherScope, other with { RequestId = op.RequestId }));
        await using var db = database.CreateDbContext();
        var original = await db.Users.FindAsync(op.Tenant.UserId);
        var user = new User { CompanyId = op.Tenant.CompanyId, EstablishmentId = op.Tenant.EstablishmentId,
            EmissionPointId = op.Tenant.EmissionPointId, RoleId = original!.RoleId, Username = "other-synthetic",
            Email = "other@synthetic.test", PasswordHash = "test-only", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Users.Add(user); await db.SaveChangesAsync();
        var context = new OperationalContext { CompanyId = op.Tenant.CompanyId, EstablishmentId = op.Tenant.EstablishmentId,
            EmissionPointId = op.Tenant.EmissionPointId, CompanyTimeZoneId = "America/Guayaquil", UserId = user.Id, Username = user.Username };
        await using var actor = new TestServiceScope(database, context);
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => Send(actor, op))).Message);
    }

    [Theory]
    [InlineData("receipt", false)] [InlineData("entry", false)] [InlineData("cash-in", false)]
    [InlineData("receipt", true)] [InlineData("entry", true)] [InlineData("cash-in", true)]
    [InlineData("open", false)] [InlineData("open", true)]
    public async Task Replay_revalidates_session_and_role_versions_before_returning_data(string kind, bool role)
    {
        var op = await Prepare(kind);
        await using var owner = new TestServiceScope(database, op.Tenant.OperationalContext);
        await Send(owner, op);
        await using var db = database.CreateDbContext();
        var user = await db.Users.Include(u => u.Role).SingleAsync(u => u.Id == op.Tenant.UserId);
        op.Tenant.OperationalContext.UserSessionVersion = user.SessionVersion;
        op.Tenant.OperationalContext.RoleAuthorizationVersion = user.Role.AuthorizationVersion;
        if (role) user.Role.AuthorizationVersion++; else user.SessionVersion++;
        await db.SaveChangesAsync();
        await using var revoked = new TestServiceScope(database, op.Tenant.OperationalContext);
        var rejection = await Assert.ThrowsAsync<OperationalContextException>(() => Send(revoked, op));
        Assert.Equal("SESSION_STALE", rejection.ErrorCode);
        Assert.Equal(401, rejection.StatusCode);
    }

    [Fact]
    public async Task Migration_model_is_current_and_legacy_metadata_is_null()
    {
        var op = await Prepare("entry");
        await using var db = database.CreateDbContext();
        Assert.False(db.Database.HasPendingModelChanges());
        var stock = await db.ProductStocks.SingleAsync(s => s.ProductId == op.Tenant.Products[1].Id);
        db.ProductStocks.Remove(stock);
        await db.SaveChangesAsync();
        await using var scope = new TestServiceScope(database, op.Tenant.OperationalContext);
        var internalMovement = await scope.Inventory.RegisterOpeningAsync(op.Tenant.Products[1].Id, 0m, 1, 1);
        var stored = await db.InventoryMovements.FindAsync(internalMovement.Id);
        Assert.Null(stored!.RequestId); Assert.Null(stored.RequestHash); Assert.Null(stored.RequestEmissionPointId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Opening_same_key_serializes_and_recovers_original_even_after_close_and_new_open(bool changed)
    {
        var op = await Prepare("open");
        var held = new AsyncTestSignal(); var release = new AsyncTestSignal();
        await using var owner = new TestServiceScope(database, op.Tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, held),
            SqlCommandGateInterceptor.WaitBefore(sql => sql.Contains("FROM \"CashSessions\""), release));
        await using var waiter = new TestServiceScope(database, op.Tenant.OperationalContext);
        Task<int>? first = null; Task<int>? second = null;
        try
        {
            first = Send(owner, op); await held.WaitAsync();
            second = Send(waiter, changed ? op with { Quantity = 3m } : op);
            await WaitBlocked(waiter, owner);
            Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        }
        finally { release.Set(); }
        var originalId = await first!.WaitAsync(TimeSpan.FromSeconds(30));
        if (changed) Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => second!.WaitAsync(TimeSpan.FromSeconds(30)))).Message);
        else Assert.Equal(originalId, await second!.WaitAsync(TimeSpan.FromSeconds(30)));
        await using var close = new TestServiceScope(database, op.Tenant.OperationalContext);
        await close.CashSessions.CloseAsync(originalId, new() { CountedCashAmount = 2m });
        var later = await close.CashSessions.OpenAsync(new() { RequestId = Guid.NewGuid(), OpeningAmount = 9m });
        await using var retry = new TestServiceScope(database, op.Tenant.OperationalContext);
        var recovered = await retry.CashSessions.OpenAsync(new() { RequestId = op.RequestId,
            OpeningAmount = 2.00001m, OpeningNotes = "  " + op.Notes + "  " });
        Assert.Equal(originalId, recovered.Id); Assert.Equal(op.RequestId, recovered.RequestId);
        Assert.Equal(CashSessionStatus.Closed, recovered.Status);
        Assert.Equal(2m, recovered.OpeningAmount);
        Assert.Equal(later.Id, (await retry.CashSessions.GetCurrentAsync())!.Id);
        await using var db = database.CreateDbContext();
        Assert.Equal(3, await db.CashSessions.CountAsync());
        Assert.Empty(await db.CashMovements.ToListAsync());
    }

    [Theory]
    [InlineData("receipt", false)] [InlineData("entry", false)] [InlineData("cash-in", false)] [InlineData("open", false)]
    [InlineData("receipt", true)] [InlineData("entry", true)] [InlineData("cash-in", true)] [InlineData("open", true)]
    public async Task Replay_conflicts_in_another_current_authorized_point_or_establishment(string kind, bool changeEstablishment)
    {
        var op = await Prepare(kind);
        await using var original = new TestServiceScope(database, op.Tenant.OperationalContext);
        await Send(original, op);
        await using var db = database.CreateDbContext();
        var establishmentId = op.Tenant.EstablishmentId;
        if (changeEstablishment)
        {
            var establishment = new Establishment { CompanyId = op.Tenant.CompanyId, Code = "002",
                Name = "Synthetic destination", Address = "Synthetic", IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Establishments.Add(establishment); await db.SaveChangesAsync();
            establishmentId = establishment.Id;
        }
        var point = new EmissionPoint { EstablishmentId = establishmentId, Code = changeEstablishment ? "001" : "002",
            Name = "Synthetic point", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.EmissionPoints.Add(point); await db.SaveChangesAsync();
        var user = await db.Users.FindAsync(op.Tenant.UserId);
        user!.EstablishmentId = establishmentId; user.EmissionPointId = point.Id; await db.SaveChangesAsync();
        var context = new OperationalContext { CompanyId = op.Tenant.CompanyId, EstablishmentId = establishmentId,
            EmissionPointId = point.Id, UserId = user.Id, Username = user.Username, CompanyTimeZoneId = "America/Guayaquil" };
        await using var moved = new TestServiceScope(database, context);
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => Send(moved, op))).Message);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Distinct_inventory_keys_serialize_absent_stock_creation_and_never_overdraw(bool exit)
    {
        var op = await Prepare(exit ? "exit" : "entry");
        if (!exit)
        {
            await using var db = database.CreateDbContext();
            db.ProductStocks.Remove(await db.ProductStocks.SingleAsync(s => s.ProductId == op.Tenant.Products[0].Id));
            await db.SaveChangesAsync();
        }
        op = op with { Quantity = exit ? 6m : 2m };
        var held = new AsyncTestSignal(); var release = new AsyncTestSignal();
        await using var owner = new TestServiceScope(database, op.Tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, held),
            SqlCommandGateInterceptor.WaitBefore(sql => sql.Contains("FROM \"Products\""), release));
        await using var waiter = new TestServiceScope(database, op.Tenant.OperationalContext);
        Task<int>? first = null; Task<int>? second = null;
        try
        {
            first = Send(owner, op); await held.WaitAsync();
            second = Send(waiter, op with { RequestId = Guid.NewGuid() });
            await WaitBlocked(waiter, owner);
        }
        finally { release.Set(); }
        await first!.WaitAsync(TimeSpan.FromSeconds(30));
        if (exit) Assert.Equal("INSUFFICIENT_STOCK", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => second!.WaitAsync(TimeSpan.FromSeconds(30)))).Message);
        else Assert.True(await second!.WaitAsync(TimeSpan.FromSeconds(30)) > 0);
        await using var verify = database.CreateDbContext();
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(verify, op.Tenant, op.Tenant.Products[0].Id));
        Assert.Equal(exit ? 1 : 2, await verify.InventoryMovements.CountAsync());
        Assert.Single(await verify.ProductStocks.Where(s => s.ProductId == op.Tenant.Products[0].Id).ToListAsync());
    }

    private sealed class FailMovement(int productId, bool cash) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (cash ? db.ChangeTracker.Entries<CashMovement>().Any(e => e.State == EntityState.Added)
                : db.ChangeTracker.Entries<InventoryMovement>().Any(e => e.State == EntityState.Added && e.Entity.ProductId == productId))
                throw new InvalidOperationException("SYNTHETIC_MOVEMENT_FAILURE");
            return ValueTask.FromResult(result);
        }
    }

    private async Task WaitBlocked(TestServiceScope waiter, TestServiceScope owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(timeout.Token);
        var ownerId = ((NpgsqlConnection)owner.DbContext.Database.GetDbConnection()).ProcessID;
        while (true)
        {
            if (waiter.DbContext.Database.GetDbConnection() is NpgsqlConnection { State: System.Data.ConnectionState.Open } connection)
            {
                await using var command = new NpgsqlCommand("""
                    WITH RECURSIVE blockers(pid) AS (
                        SELECT unnest(pg_blocking_pids(@pid))
                        UNION SELECT unnest(pg_blocking_pids(pid)) FROM blockers
                    ) SELECT ARRAY(SELECT pid FROM blockers ORDER BY pid)
                    """, observer);
                command.Parameters.AddWithValue("pid", connection.ProcessID);
                var blockers = (int[])(await command.ExecuteScalarAsync(timeout.Token))!;
                if (blockers.Contains(ownerId))
                {
                    Console.WriteLine($"529 lock proof: waiter={connection.ProcessID}, owner={ownerId}, blockers=[{string.Join(",", blockers)}]");
                    return;
                }
            }
            await Task.Delay(10, timeout.Token);
        }
    }
}
