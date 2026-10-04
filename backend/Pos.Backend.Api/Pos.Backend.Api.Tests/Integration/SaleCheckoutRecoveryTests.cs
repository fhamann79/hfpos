using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
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
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class SaleCheckoutRecoveryTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static SaleCreateDto Request(TestTenant tenant) => new()
    {
        RequestId = Guid.NewGuid(), CashReceived = 20m,
        Items = [new() { ProductId = tenant.Products[0].Id, Quantity = 1m, UnitPrice = 10m }]
    };

    private async Task<int> Open(TestTenant tenant)
    {
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        return (await scope.CashSessions.OpenAsync(new() { OpeningAmount = 5m })).Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_cash_movement_and_sale_take_company_before_cash_in_both_orders(bool movementFirst)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "cash-order", 812, 5m);
        var cash = await Open(tenant);
        var companyAcquired = new AsyncTestSignal();
        var releaseCash = new AsyncTestSignal();
        var winnerInterceptors = new Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] {
            SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, companyAcquired),
            SqlCommandGateInterceptor.WaitBefore(movementFirst
                ? SqlCommandMatchers.CashSessionByIdForUpdate : SqlCommandMatchers.OpenCashSessionForUpdate, releaseCash)
        };
        await using var movement = new TestServiceScope(database, tenant.OperationalContext,
            movementFirst ? winnerInterceptors : []);
        await using var sale = new TestServiceScope(database, tenant.OperationalContext,
            movementFirst ? [] : winnerInterceptors);
        Task? first = null;
        Task? second = null;
        Task Move() => movement.CashSessions.AddMovementAsync(cash,
            new() { Type = CashMovementType.CashIn, Amount = 2m, Reason = "Synthetic manual cash" });
        Task Sell() => sale.Sales.CreateAsync(Request(tenant));
        try
        {
            first = movementFirst ? Move() : Sell();
            await companyAcquired.WaitAsync();
            second = movementFirst ? Sell() : Move();
            await WaitBlocked(movementFirst ? sale : movement, (movementFirst ? movement : sale).DbContext);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            releaseCash.Set();
            await Task.WhenAll(new[] { first, second }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(30));
        }
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Single(await verify.CashMovements.ToListAsync());
        Assert.Single(await verify.DocumentSequences.ToListAsync());
        Assert.Single(await verify.InventoryMovements.ToListAsync());
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(verify, tenant, tenant.Products[0].Id));
        await using var final = new TestServiceScope(database, tenant.OperationalContext);
        var session = await final.CashSessions.GetByIdAsync(cash);
        Assert.NotNull(session);
        Assert.Equal(10m, session.CashSalesAmount);
        Assert.Equal(2m, session.CashInAmount);
        Assert.Equal(17m, session.ExpectedCashAmount);
    }

    [Fact]
    public async Task Normalized_notes_limit_returns_definitive_400_without_effects_and_uuid_remains_retryable()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "notes", 813, 5m);
        await Open(tenant);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var controller = new SalesController(scope.Sales, null!, null!, null!, null!);
        var request = Request(tenant); request.Notes = "  " + new string('x', 501) + "  ";
        var response = await controller.Create(request);
        var rejection = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.Equal("SALE_NOTES_TOO_LONG", Assert.IsType<ApiErrorResponse>(rejection.Value).Error);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync());
        Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        request.Notes = "  " + new string('x', 500) + "  ";
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Equal(new string('x', 500), sale.Notes);
        Assert.Equal(request.RequestId, sale.RequestId);
        request.Notes = new string('x', 500);
        Assert.Equal(sale.Id, (await scope.Sales.CreateAsync(request)).Id);
        Assert.Single(await scope.DbContext.Sales.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_same_request_and_lost_response_have_one_effect_and_replay_after_close(bool invoice)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "recover", 801, 5m);
        var cash = await Open(tenant);
        await using (var setup = database.CreateDbContext())
        {
            (await setup.Companies.SingleAsync()).MatrixAddress = "Synthetic matrix address";
            var product = await setup.Products.SingleAsync();
            product.InternalCode = "SYN-801";
            await setup.SaveChangesAsync();
        }
        var request = Request(tenant);
        request.DocumentType = invoice ? SaleDocumentType.Invoice : SaleDocumentType.Ticket;
        await using var owner = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(owner).BeginChangeAsync(tenant.CompanyId);
        await using var a = new TestServiceScope(database, tenant.OperationalContext);
        await using var b = new TestServiceScope(database, tenant.OperationalContext);
        var first = DraftService(a, tenant).CreateAsync(request);
        var second = DraftService(b, tenant).CreateAsync(request);
        try
        {
            await WaitBlocked(a, owner);
            await WaitBlocked(b, owner);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            // Release the test-only owner before draining or disposing waiting scopes.
            await tx.RollbackAsync();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(results[0].Id, results[1].Id);
        var originalName = results[0].Items[0].ProductNameSnapshot;
        await using (var close = new TestServiceScope(database, tenant.OperationalContext))
            await close.CashSessions.CloseAsync(cash, new() { CountedCashAmount = 15m });
        await using (var edit = database.CreateDbContext())
        {
            var product = await edit.Products.SingleAsync();
            product.Name = "Changed live name"; product.InternalCode = "CHANGED"; product.IsActive = false;
            (await edit.ProductStocks.SingleAsync()).Quantity = 0;
            (await edit.Companies.SingleAsync()).Ruc = "changed-invalid-fiscal-configuration";
            await edit.SaveChangesAsync();
        }
        // Discard the first HTTP-equivalent result and recover from an independent scope.
        await using var retry = new TestServiceScope(database, tenant.OperationalContext);
        var recovered = await DraftService(retry, tenant).CreateAsync(request);
        Assert.Equal(results[0].Id, recovered.Id);
        Assert.Equal(originalName, recovered.Items[0].ProductNameSnapshot);
        Assert.Equal(originalName, recovered.Items[0].ProductName);
        Assert.Equal("SYN-801", recovered.Items[0].ProductSkuSnapshot);
        Assert.Equal(20m, recovered.CashReceived); Assert.Equal(10m, recovered.CashChange);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Single(await verify.InventoryMovements.Where(m => m.SourceType == InventoryMovementSourceType.Sale).ToListAsync());
        Assert.Single(await verify.DocumentSequences.ToListAsync());
        Assert.Equal(10m, (await verify.CashSessions.SingleAsync()).CashSalesAmount);
        var sale = await verify.Sales.SingleAsync();
        Assert.Equal(1, sale.Sequential);
        if (invoice) Assert.False(string.IsNullOrWhiteSpace(sale.SriXmlDraft));
        else Assert.Null(sale.SriXmlDraft);
    }

    [Fact]
    public async Task Material_changes_including_duplicate_line_order_conflict_without_consuming_effects()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "conflict", 802, 10m);
        await Open(tenant);
        var request = Request(tenant);
        request.Items.Add(new() { ProductId = tenant.Products[0].Id, Quantity = 2m, UnitPrice = 2m });
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var original = await scope.Sales.CreateAsync(request);
        request.Items.Reverse();
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.Items.Reverse(); request.CashReceived = 30m;
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.CashReceived = 20m; request.Notes = "changed";
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.Notes = "  "; request.PaymentMethod = SalePaymentMethod.Cash; request.DocumentType = SaleDocumentType.Ticket;
        request.DiscountAmount = 0; request.Items[0].DiscountAmount = 0;
        Assert.Equal(original.Id, (await scope.Sales.CreateAsync(request)).Id);
        Assert.Equal(2, (await scope.Sales.GetByIdAsync(original.Id))!.Items.Count);
        Assert.Equal(7m, await TestDataBuilder.GetStockAsync(scope.DbContext, tenant, tenant.Products[0].Id));
    }

    [Theory]
    [InlineData(9, "CASH_RECEIVED_INSUFFICIENT")]
    [InlineData(-1, "CASH_RECEIVED_INVALID")]
    public async Task Invalid_cash_rolls_back_and_same_context_can_retry(int received, string error)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "cash", 803, 5m);
        await Open(tenant);
        var request = Request(tenant); request.CashReceived = received;
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        Assert.Equal(error, (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync());
        Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        request.CashReceived = 20m;
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Equal(10m, sale.Total); Assert.Equal(10m, sale.CashChange);
        Assert.Equal(15m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
    }

    [Theory]
    [InlineData(SalePaymentMethod.Card)]
    [InlineData(SalePaymentMethod.Transfer)]
    [InlineData(SalePaymentMethod.Other)]
    public async Task Noncash_preserves_total_only_and_rejects_tendered(SalePaymentMethod method)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "noncash", 804, 5m);
        await Open(tenant);
        var request = Request(tenant); request.PaymentMethod = method;
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        Assert.Equal("CASH_RECEIVED_NOT_APPLICABLE", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.CashReceived = null;
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Null(sale.CashReceived); Assert.Null(sale.CashChange);
        Assert.Equal(10m, sale.Total);
        Assert.Equal(5m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
    }

    [Fact]
    public async Task Zero_total_rounding_precision_overflow_and_uuid_validation_are_explicit()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "bounds", 805, 5m);
        await Open(tenant);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); request.RequestId = Guid.Empty;
        Assert.Equal("SALE_REQUEST_ID_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.RequestId = Guid.NewGuid(); request.Items[0].Quantity = 0.00001m;
        Assert.Equal("SALE_AMOUNT_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.Items[0].Quantity = 99999999999999.9999m; request.Items[0].UnitPrice = 9999999999999999.99m;
        Assert.Equal("SALE_AMOUNT_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.Items[0].Quantity = 1; request.Items[0].UnitPrice = 0; request.CashReceived = 0;
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Equal(0m, sale.Total); Assert.Equal(0m, sale.CashReceived); Assert.Equal(0m, sale.CashChange);
        Assert.Equal(5m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
    }

    [Fact]
    public async Task Stock_failure_rolls_back_whole_transaction_and_retry_same_scope_is_safe()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock", 806, 5m, 0m);
        await Open(tenant);
        var request = Request(tenant); request.CashReceived = 30m;
        request.Items.Add(new() { ProductId = tenant.Products[1].Id, Quantity = 1, UnitPrice = 10 });
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        Assert.Equal("INSUFFICIENT_STOCK", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        Assert.Empty(await scope.DbContext.Sales.ToListAsync());
        Assert.Empty(await scope.DbContext.InventoryMovements.ToListAsync());
        Assert.Empty(await scope.DbContext.DocumentSequences.ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(scope.DbContext, tenant, tenant.Products[0].Id));
        request.Items.RemoveAt(1);
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Equal(1, sale.Sequential);
        Assert.Single(await scope.DbContext.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Cash_uses_existing_away_from_zero_money_rounding_and_validates_price_scale()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "rounding", 811, 5m);
        await Open(tenant);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var request = Request(tenant); request.CashReceived = 20.005m; request.Items[0].UnitPrice = 10.001m;
        Assert.Equal("SALE_AMOUNT_INVALID", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Sales.CreateAsync(request))).Message);
        request.Items[0].UnitPrice = 10m;
        var sale = await scope.Sales.CreateAsync(request);
        Assert.Equal(20.01m, sale.CashReceived); Assert.Equal(10.01m, sale.CashChange);
        Assert.Equal(15m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_session_or_role_revocation_while_waiting_blocks_create_and_replay(bool role)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "revoke", 807, 5m);
        await Open(tenant);
        tenant.OperationalContext.UserSessionVersion = 1; tenant.OperationalContext.RoleAuthorizationVersion = 1;
        var request = Request(tenant);
        await using (var first = new TestServiceScope(database, tenant.OperationalContext)) await first.Sales.CreateAsync(request);
        await using var owner = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(owner).BeginChangeAsync(tenant.CompanyId);
        await using var waiter = new TestServiceScope(database, tenant.OperationalContext);
        var pending = waiter.Sales.CreateAsync(request);
        await WaitBlocked(waiter, owner);
        if (role) (await owner.Roles.SingleAsync()).AuthorizationVersion++;
        else (await owner.Users.SingleAsync()).SessionVersion++;
        await owner.SaveChangesAsync(); await tx.CommitAsync();
        Assert.Equal("SESSION_STALE", (await Assert.ThrowsAsync<OperationalContextException>(() => pending)).ErrorCode);
        request.RequestId = Guid.NewGuid();
        Assert.Equal("SESSION_STALE", (await Assert.ThrowsAsync<OperationalContextException>(() => waiter.Sales.CreateAsync(request))).ErrorCode);
        Assert.Single(await owner.Sales.ToListAsync());
    }

    [Fact]
    public async Task Tenant_actor_and_operational_context_cannot_recover_another_sale()
    {
        var a = await TestDataBuilder.CreateTenantAsync(database, "tenant-a", 808, 5m);
        var b = await TestDataBuilder.CreateTenantAsync(database, "tenant-b", 809, 5m);
        await Open(a); await Open(b);
        var request = Request(a);
        await using var first = new TestServiceScope(database, a.OperationalContext);
        var sale = await first.Sales.CreateAsync(request);
        await using var otherTenant = new TestServiceScope(database, b.OperationalContext);
        Assert.Null(await otherTenant.Sales.GetByIdAsync(sale.Id));
        Assert.Equal("PRODUCT_NOT_FOUND", (await Assert.ThrowsAsync<KeyNotFoundException>(() => otherTenant.Sales.CreateAsync(request))).Message);
        await using var setup = database.CreateDbContext();
        var original = await setup.Users.SingleAsync(u => u.Id == a.UserId);
        var secondUser = new User { Username = "other-synthetic-cashier", Email = "other@hfpos.test", PasswordHash = "synthetic-only",
            CompanyId = a.CompanyId, EstablishmentId = a.EstablishmentId, EmissionPointId = a.EmissionPointId,
            RoleId = original.RoleId, IsActive = true, CreatedAt = DateTime.UtcNow };
        setup.Users.Add(secondUser); await setup.SaveChangesAsync();
        var secondContext = new OperationalContext { CompanyId = a.CompanyId, EstablishmentId = a.EstablishmentId,
            EmissionPointId = a.EmissionPointId, UserId = secondUser.Id, Username = secondUser.Username, CompanyTimeZoneId = "America/Guayaquil" };
        await using var otherActor = new TestServiceScope(database, secondContext);
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => otherActor.Sales.CreateAsync(request))).Message);
        var point = new EmissionPoint { EstablishmentId = a.EstablishmentId, Code = "002", Name = "Synthetic other point", IsActive = true, CreatedAt = DateTime.UtcNow };
        setup.EmissionPoints.Add(point); await setup.SaveChangesAsync(); original.EmissionPointId = point.Id; await setup.SaveChangesAsync();
        a.OperationalContext.EmissionPointId = point.Id;
        await using var moved = new TestServiceScope(database, a.OperationalContext);
        Assert.Equal("REQUEST_CONFLICT", (await Assert.ThrowsAsync<InvalidOperationException>(() => moved.Sales.CreateAsync(request))).Message);
    }

    [Fact]
    public async Task Additive_migration_preserves_legacy_rows_without_fabricating_history()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "legacy", 810, 5m);
        await Open(tenant);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var sale = await scope.Sales.CreateAsync(Request(tenant));
        var migrator = scope.DbContext.GetService<IMigrator>();
        await migrator.MigrateAsync("20261004050131_AddInitialDataBatches");
        await migrator.MigrateAsync();
        scope.DbContext.ChangeTracker.Clear();
        var legacy = await scope.Sales.GetByIdAsync(sale.Id);
        Assert.NotNull(legacy); Assert.Equal(sale.Total, legacy.Total); Assert.Equal(sale.Number, legacy.Number);
        Assert.Null(legacy.RequestId); Assert.Null(legacy.CashReceived); Assert.Null(legacy.CashChange);
        Assert.Null(legacy.Items[0].ProductNameSnapshot); Assert.Null(legacy.Items[0].ProductSkuSnapshot);
        Assert.False(scope.DbContext.Database.HasPendingModelChanges());
    }

    private SalesService DraftService(TestServiceScope scope, TestTenant tenant)
    {
        var clock = new FixedBusinessClock(); var fiscalClock = new FixedSriFiscalClock(clock.UtcNow);
        return new SalesService(scope.DbContext, NullLogger<SalesService>.Instance,
            new StaticOperationalContextAccessor(tenant.OperationalContext), scope.Inventory, scope.CashSessions,
            new FiscalDocumentNumberService(scope.DbContext, NullLogger<FiscalDocumentNumberService>.Instance, fiscalClock),
            new SriAccessKeyService(), new SriXmlDraftService(), fiscalClock, clock, new SriInvoiceXmlValidator(),
            Options.Create(new SriOptions { Environment = 1, EmissionType = 1 }), new TenantAdministrationGuard(scope.DbContext));
    }

    private async Task WaitBlocked(TestServiceScope waiter, Pos.Backend.Api.Infrastructure.Data.PosDbContext owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(timeout.Token);
        var ownerId = ((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID;
        int? waiterId = null;
        int[] blockers = [];
        while (true)
        {
            if (waiter.DbContext.Database.GetDbConnection() is NpgsqlConnection { State: System.Data.ConnectionState.Open } connection)
            {
                waiterId = connection.ProcessID;
                // A queued row-lock waiter can be blocked by the preceding waiter, not the owner directly.
                await using var command = new NpgsqlCommand("""
                    WITH RECURSIVE blockers(pid) AS (
                        SELECT unnest(pg_blocking_pids(@pid))
                        UNION
                        SELECT unnest(pg_blocking_pids(pid)) FROM blockers
                    )
                    SELECT ARRAY(SELECT pid FROM blockers ORDER BY pid)
                    """, observer);
                command.Parameters.AddWithValue("pid", waiterId.Value);
                blockers = (int[])(await command.ExecuteScalarAsync(timeout.Token))!;
                if (blockers.Contains(ownerId))
                {
                    Console.WriteLine($"Lock proof: waiter={waiterId}, owner={ownerId}, blocker chain=[{string.Join(",", blockers)}]");
                    return;
                }
            }
            try
            {
                await Task.Delay(10, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new Xunit.Sdk.XunitException($"Owner lock not observed: waiter={waiterId}, owner={ownerId}, blocker chain=[{string.Join(",", blockers)}]");
            }
        }
    }
}
