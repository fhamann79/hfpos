using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class SalesCashIntegrityTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private const decimal OpeningAmount = 20m;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(45);

    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_without_open_cash_session_leaves_no_partial_sale_or_inventory()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "no-cash", 1, 5m);

        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => services.Sales.CreateAsync(CreateCashSaleRequest(tenant.Products[0])));

            Assert.Equal("CASH_SESSION_REQUIRED", exception.Message);
        }

        await using var verification = database.CreateDbContext();
        Assert.Empty(await verification.Sales.ToListAsync());
        Assert.Empty(await verification.InventoryMovements.ToListAsync());
        Assert.Empty(await verification.DocumentSequences.ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Create_wins_over_close_and_closed_snapshot_includes_the_sale()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "create-wins", 2, 5m);
        var sessionId = await OpenCashSessionAsync(tenant);
        var createLockAcquired = new AsyncTestSignal();

        await using var createServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(
                SqlCommandMatchers.OpenCashSessionForUpdate,
                createLockAcquired));
        await using var closeServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.WaitBefore(
                SqlCommandMatchers.CashSessionByIdForUpdate,
                createLockAcquired));

        var closeOutcomeTask = CaptureAsync(closeServices.CashSessions.CloseAsync(
            sessionId,
            new CloseCashSessionDto { CountedCashAmount = 30m }));
        var createOutcomeTask = CaptureAsync(createServices.Sales.CreateAsync(
            CreateCashSaleRequest(tenant.Products[0])));

        await Task.WhenAll(new Task[] { createOutcomeTask, closeOutcomeTask })
            .WaitAsync(OperationTimeout);

        var createOutcome = await createOutcomeTask;
        var closeOutcome = await closeOutcomeTask;
        Assert.Null(createOutcome.Error);
        Assert.NotNull(createOutcome.Value);
        Assert.Null(closeOutcome.Error);
        Assert.NotNull(closeOutcome.Value);

        await using var verification = database.CreateDbContext();
        var sale = await verification.Sales.SingleAsync();
        var session = await verification.CashSessions.SingleAsync();

        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(sessionId, sale.CashSessionId);
        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal(10m, session.CashSalesAmount);
        Assert.Equal(30m, session.ExpectedCashAmount);
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
        Assert.Single(await verification.InventoryMovements
            .Where(movement => movement.SourceType == InventoryMovementSourceType.Sale)
            .ToListAsync());
    }

    [Fact]
    public async Task Close_wins_over_create_and_create_rolls_back_without_consuming_stock_or_number()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "close-wins", 3, 5m);
        var sessionId = await OpenCashSessionAsync(tenant);
        var closeLockAcquired = new AsyncTestSignal();

        await using var createServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.WaitBefore(
                SqlCommandMatchers.OpenCashSessionForUpdate,
                closeLockAcquired));
        await using var closeServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(
                SqlCommandMatchers.CashSessionByIdForUpdate,
                closeLockAcquired));

        var createOutcomeTask = CaptureAsync(createServices.Sales.CreateAsync(
            CreateCashSaleRequest(tenant.Products[0])));
        var closeOutcomeTask = CaptureAsync(closeServices.CashSessions.CloseAsync(
            sessionId,
            new CloseCashSessionDto { CountedCashAmount = OpeningAmount }));

        await Task.WhenAll(new Task[] { createOutcomeTask, closeOutcomeTask })
            .WaitAsync(OperationTimeout);

        var createOutcome = await createOutcomeTask;
        var closeOutcome = await closeOutcomeTask;
        Assert.Equal("CASH_SESSION_REQUIRED", createOutcome.Error?.Message);
        Assert.Null(createOutcome.Value);
        Assert.Null(closeOutcome.Error);
        Assert.NotNull(closeOutcome.Value);

        await using var verification = database.CreateDbContext();
        var session = await verification.CashSessions.SingleAsync();

        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal(0m, session.CashSalesAmount);
        Assert.Equal(OpeningAmount, session.ExpectedCashAmount);
        Assert.Empty(await verification.Sales.ToListAsync());
        Assert.Empty(await verification.InventoryMovements.ToListAsync());
        Assert.Empty(await verification.DocumentSequences.ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Void_cash_sale_with_original_open_session_restores_stock_without_cash_out()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "void-open", 4, 5m);
        var (sessionId, saleId) = await OpenAndCreateCashSaleAsync(tenant);

        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            await services.Sales.VoidAsync(saleId, new VoidSaleDto { Reason = "Operator correction" });

            var liveSession = await services.CashSessions.GetByIdAsync(sessionId);
            Assert.NotNull(liveSession);
            Assert.Equal(0m, liveSession.CashSalesAmount);
            Assert.Equal(OpeningAmount, liveSession.ExpectedCashAmount);
            Assert.Equal(0m, liveSession.CashOutAmount);
        }

        await using var verification = database.CreateDbContext();
        var sale = await verification.Sales.SingleAsync();

        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal(SaleVoidCashEffect.OriginalOpenSessionRecalculated, sale.VoidCashEffect);
        Assert.Equal(sessionId, sale.VoidCashSessionId);
        Assert.Null(sale.VoidCashMovementId);
        Assert.Equal("Operator correction", sale.VoidReason);
        Assert.Equal(tenant.UserId, sale.VoidedByUserId);
        Assert.Empty(await verification.CashMovements.ToListAsync());
        Assert.Single(await verification.InventoryMovements
            .Where(movement => movement.SourceType == InventoryMovementSourceType.SaleVoid)
            .ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Void_cash_sale_after_close_preserves_original_snapshot_and_creates_one_current_cash_out()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "void-closed", 5, 5m);
        var (originalSessionId, saleId) = await OpenAndCreateCashSaleAsync(tenant);
        await CloseCashSessionAsync(tenant, originalSessionId, 30m);
        var currentSessionId = await OpenCashSessionAsync(tenant);

        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            await services.Sales.VoidAsync(saleId, new VoidSaleDto { Reason = "Post-close return" });

            var currentSession = await services.CashSessions.GetByIdAsync(currentSessionId);
            Assert.NotNull(currentSession);
            Assert.Equal(10m, currentSession.CashOutAmount);
            Assert.Equal(10m, currentSession.ExpectedCashAmount);
        }

        await using var verification = database.CreateDbContext();
        var originalSession = await verification.CashSessions.SingleAsync(
            session => session.Id == originalSessionId);
        var sale = await verification.Sales.SingleAsync();
        var cashOut = await verification.CashMovements.SingleAsync();

        Assert.Equal(CashSessionStatus.Closed, originalSession.Status);
        Assert.Equal(10m, originalSession.CashSalesAmount);
        Assert.Equal(30m, originalSession.ExpectedCashAmount);
        Assert.Equal(30m, originalSession.CountedCashAmount);
        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal(SaleVoidCashEffect.CurrentSessionCashOut, sale.VoidCashEffect);
        Assert.Equal(currentSessionId, sale.VoidCashSessionId);
        Assert.Equal(cashOut.Id, sale.VoidCashMovementId);
        Assert.Equal(CashMovementType.CashOut, cashOut.Type);
        Assert.Equal(10m, cashOut.Amount);
        Assert.Equal(currentSessionId, cashOut.CashSessionId);
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Void_cash_sale_after_close_without_current_session_is_fully_rejected()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "void-no-current", 6, 5m);
        var (originalSessionId, saleId) = await OpenAndCreateCashSaleAsync(tenant);
        await CloseCashSessionAsync(tenant, originalSessionId, 30m);

        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => services.Sales.VoidAsync(
                    saleId,
                    new VoidSaleDto { Reason = "No current cash session" }));

            Assert.Equal("SALE_VOID_CASH_SESSION_REQUIRED", exception.Message);
        }

        await using var verification = database.CreateDbContext();
        var sale = await verification.Sales.SingleAsync();
        var originalSession = await verification.CashSessions.SingleAsync();

        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Null(sale.VoidedAt);
        Assert.Null(sale.VoidReason);
        Assert.Null(sale.VoidCashEffect);
        Assert.Empty(await verification.CashMovements.ToListAsync());
        Assert.Empty(await verification.InventoryMovements
            .Where(movement => movement.SourceType == InventoryMovementSourceType.SaleVoid)
            .ToListAsync());
        Assert.Equal(CashSessionStatus.Closed, originalSession.Status);
        Assert.Equal(10m, originalSession.CashSalesAmount);
        Assert.Equal(30m, originalSession.ExpectedCashAmount);
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Concurrent_double_void_restores_stock_and_creates_cash_out_exactly_once()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "double-void", 7, 5m);
        var (originalSessionId, saleId) = await OpenAndCreateCashSaleAsync(tenant);
        await CloseCashSessionAsync(tenant, originalSessionId, 30m);
        var currentSessionId = await OpenCashSessionAsync(tenant);
        var winnerLockAcquired = new AsyncTestSignal();

        await using var loserServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.WaitBefore(
                SqlCommandMatchers.SaleByIdForUpdate,
                winnerLockAcquired));
        await using var winnerServices = new TestServiceScope(
            database,
            tenant.OperationalContext,
            SqlCommandGateInterceptor.SignalAfter(
                SqlCommandMatchers.SaleByIdForUpdate,
                winnerLockAcquired));

        var loserOutcomeTask = CaptureAsync(loserServices.Sales.VoidAsync(
            saleId,
            new VoidSaleDto { Reason = "Concurrent loser" }));
        var winnerOutcomeTask = CaptureAsync(winnerServices.Sales.VoidAsync(
            saleId,
            new VoidSaleDto { Reason = "Concurrent winner" }));

        await Task.WhenAll(new Task[] { loserOutcomeTask, winnerOutcomeTask })
            .WaitAsync(OperationTimeout);

        var loserOutcome = await loserOutcomeTask;
        var winnerOutcome = await winnerOutcomeTask;
        Assert.Equal("SALE_ALREADY_VOIDED", loserOutcome.Error?.Message);
        Assert.Null(loserOutcome.Value);
        Assert.Null(winnerOutcome.Error);
        Assert.NotNull(winnerOutcome.Value);

        await using var verification = database.CreateDbContext();
        var sale = await verification.Sales.SingleAsync();
        var cashOuts = await verification.CashMovements
            .Where(movement => movement.CashSessionId == currentSessionId
                && movement.Type == CashMovementType.CashOut)
            .ToListAsync();
        var voidMovements = await verification.InventoryMovements
            .Where(movement => movement.SourceType == InventoryMovementSourceType.SaleVoid)
            .ToListAsync();

        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal("Concurrent winner", sale.VoidReason);
        Assert.Single(cashOuts);
        Assert.Single(voidMovements);
        Assert.Equal(cashOuts[0].Id, sale.VoidCashMovementId);
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Tenant_cannot_void_another_company_sale_by_id()
    {
        var tenantA = await TestDataBuilder.CreateTenantAsync(database, "tenant-a", 8, 5m);
        var tenantB = await TestDataBuilder.CreateTenantAsync(database, "tenant-b", 9, 5m);
        var (_, tenantBSaleId) = await OpenAndCreateCashSaleAsync(tenantB);

        await using (var services = new TestServiceScope(database, tenantA.OperationalContext))
        {
            var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
                () => services.Sales.VoidAsync(
                    tenantBSaleId,
                    new VoidSaleDto { Reason = "Cross-tenant attempt" }));

            Assert.Equal("SALE_NOT_FOUND", exception.Message);
        }

        await using var verification = database.CreateDbContext();
        var tenantBSale = await verification.Sales.SingleAsync(sale => sale.Id == tenantBSaleId);

        Assert.Equal(tenantB.CompanyId, tenantBSale.CompanyId);
        Assert.Equal(SaleStatus.Completed, tenantBSale.Status);
        Assert.Null(tenantBSale.VoidedAt);
        Assert.Empty(await verification.InventoryMovements
            .Where(movement => movement.SourceType == InventoryMovementSourceType.SaleVoid)
            .ToListAsync());
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(
            verification, tenantB, tenantB.Products[0].Id));
    }

    [Fact]
    public async Task Inventory_failure_after_first_item_rolls_back_sale_sequence_and_all_stock_changes()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "rollback", 10, 5m, 0m);
        await OpenCashSessionAsync(tenant);
        var request = new SaleCreateDto
        {
            PaymentMethod = SalePaymentMethod.Cash,
            DocumentType = SaleDocumentType.Ticket,
            Items = new List<SaleItemCreateDto>
            {
                new()
                {
                    ProductId = tenant.Products[0].Id,
                    Quantity = 1m,
                    UnitPrice = tenant.Products[0].Price
                },
                new()
                {
                    ProductId = tenant.Products[1].Id,
                    Quantity = 1m,
                    UnitPrice = tenant.Products[1].Price
                }
            }
        };

        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => services.Sales.CreateAsync(request));

            Assert.Equal("INSUFFICIENT_STOCK", exception.Message);
        }

        await using var verification = database.CreateDbContext();
        Assert.Empty(await verification.Sales.ToListAsync());
        Assert.Empty(await verification.InventoryMovements.ToListAsync());
        Assert.Empty(await verification.DocumentSequences.ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[0].Id));
        Assert.Equal(0m, await TestDataBuilder.GetStockAsync(
            verification, tenant, tenant.Products[1].Id));
    }

    private async Task<int> OpenCashSessionAsync(TestTenant tenant)
    {
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var session = await services.CashSessions.OpenAsync(new OpenCashSessionDto
        {
            OpeningAmount = OpeningAmount,
            OpeningNotes = "Integration test"
        });
        return session.Id;
    }

    private async Task CloseCashSessionAsync(
        TestTenant tenant,
        int cashSessionId,
        decimal countedAmount)
    {
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        await services.CashSessions.CloseAsync(cashSessionId, new CloseCashSessionDto
        {
            CountedCashAmount = countedAmount,
            ClosingNotes = "Integration test"
        });
    }

    private async Task<(int SessionId, int SaleId)> OpenAndCreateCashSaleAsync(TestTenant tenant)
    {
        var sessionId = await OpenCashSessionAsync(tenant);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var sale = await services.Sales.CreateAsync(CreateCashSaleRequest(tenant.Products[0]));
        return (sessionId, sale.Id);
    }

    private static SaleCreateDto CreateCashSaleRequest(TestProduct product)
        => new()
        {
            PaymentMethod = SalePaymentMethod.Cash,
            DocumentType = SaleDocumentType.Ticket,
            Items = new List<SaleItemCreateDto>
            {
                new()
                {
                    ProductId = product.Id,
                    Quantity = 1m,
                    UnitPrice = product.Price
                }
            }
        };

    private static async Task<OperationOutcome<T>> CaptureAsync<T>(Task<T> operation)
    {
        try
        {
            return new OperationOutcome<T>(await operation, Error: null);
        }
        catch (Exception exception)
        {
            return new OperationOutcome<T>(Value: default, exception);
        }
    }

    private sealed record OperationOutcome<T>(T? Value, Exception? Error);
}
