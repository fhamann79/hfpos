using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PaymentSettlementFinalizationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 9, 17);
    private static readonly DateTime BeforeMidnight = new(2026, 9, 18, 4, 59, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterMidnight = new(2026, 9, 18, 5, 1, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalization_waits_for_prior_sale_or_void_and_includes_committed_event(bool voidSale)
    {
        var tenant = await TenantAsync("sale-barrier", 221);
        var saleId = voidSale ? await CreateSaleAsync(tenant, SalePaymentMethod.Card) : 0;
        var gate = new UncommittedSaveGate(context => context.ChangeTracker.Entries<Sale>()
            .Any(entry => entry.State == EntityState.Added
                || entry.State == EntityState.Modified && entry.Entity.Status == SaleStatus.Voided));
        await using var writer = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(BeforeMidnight), gate);
        await using var finalizer = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(AfterMidnight));
        await finalizer.DbContext.Database.OpenConnectionAsync();

        var writing = voidSale
            ? writer.Sales.VoidAsync(saleId, new VoidSaleDto { Reason = "Synthetic void" })
            : writer.Sales.CreateAsync(SaleRequest(tenant, SalePaymentMethod.Card));
        Task<PaymentSettlementDto>? settling = null;
        try
        {
            await gate.Saved.WaitAsync();
            settling = finalizer.Settlements.CreateAsync(SettlementRequest(SalePaymentMethod.Card));
            await AssertBlockedByAsync(settling, ProcessId(finalizer), ProcessId(writer));
        }
        finally
        {
            gate.Release.Set();
            await writing.WaitAsync(Timeout);
            if (settling is not null) await settling.WaitAsync(Timeout);
        }

        var snapshot = await settling!;
        Assert.Equal(10m, snapshot.GrossSalesAmount);
        Assert.Equal(voidSale ? 10m : 0m, snapshot.VoidAmount);
        Assert.Equal(voidSale ? 0m : 10m, snapshot.ExpectedNetAmount);
        await using var verify = database.CreateDbContext();
        var sale = await verify.Sales.SingleAsync();
        Assert.Equal(Day, sale.BusinessDate);
        if (voidSale) Assert.Equal(Day, sale.VoidBusinessDate);
    }

    [Theory]
    [InlineData(SalePaymentMethod.Card)]
    [InlineData(SalePaymentMethod.Transfer)]
    [InlineData(SalePaymentMethod.Other)]
    public async Task Finalization_waits_for_in_flight_refund_and_includes_it(SalePaymentMethod method)
    {
        var tenant = await TenantAsync("refund-barrier", 222);
        var saleId = await CreateSaleAsync(tenant, method);
        var noteId = await AddAuthorizedNoteAsync(tenant, saleId);
        var gate = new UncommittedSaveGate(context => context.ChangeTracker.Entries<CreditNoteRefund>()
            .Any(entry => entry.State == EntityState.Added));
        await using var writer = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(BeforeMidnight), gate);
        await using var finalizer = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(AfterMidnight));
        await finalizer.DbContext.Database.OpenConnectionAsync();

        var refunding = writer.Refunds.RefundAsync(noteId, new RefundCreditNoteDto { Method = method });
        Task<PaymentSettlementDto>? settling = null;
        try
        {
            await gate.Saved.WaitAsync();
            settling = finalizer.Settlements.CreateAsync(SettlementRequest(method));
            await AssertBlockedByAsync(settling, ProcessId(finalizer), ProcessId(writer));
        }
        finally
        {
            gate.Release.Set();
            await refunding.WaitAsync(Timeout);
            if (settling is not null) await settling.WaitAsync(Timeout);
        }

        var snapshot = await settling!;
        Assert.Equal(10m, snapshot.GrossSalesAmount);
        Assert.Equal(4m, snapshot.RefundAmount);
        Assert.Equal(6m, snapshot.ExpectedNetAmount);
        // A retry remains the same financial event, not another refund or cash movement.
        await writer.Refunds.RefundAsync(noteId, new RefundCreditNoteDto { Method = method });
        await using var verify = database.CreateDbContext();
        var refund = await verify.CreditNoteRefunds.SingleAsync();
        Assert.Equal(Day, refund.BusinessDate);
        Assert.Null(refund.CashSessionId);
        Assert.Null(refund.CashMovementId);
        Assert.Empty(await verify.CashMovements.ToListAsync());
    }

    [Fact]
    public async Task Finalization_blocks_new_operational_writers_until_rollback()
    {
        var tenant = await TenantAsync("reverse-barrier", 223);
        var saleId = await CreateSaleAsync(tenant, SalePaymentMethod.Card);
        var noteId = await AddAuthorizedNoteAsync(tenant, saleId);
        await using var finalizer = new TestServiceScope(database, tenant.OperationalContext);
        await using var transaction = await finalizer.DbContext.Database.BeginTransactionAsync();
        await new TenantAdministrationGuard(finalizer.DbContext)
            .LockPaymentSettlementFinalizationAsync(tenant.OperationalContext);
        await using var writer = new TestServiceScope(database, tenant.OperationalContext);
        await writer.DbContext.Database.OpenConnectionAsync();
        var refunding = writer.Refunds.RefundAsync(noteId,
            new RefundCreditNoteDto { Method = SalePaymentMethod.Card });
        try
        {
            await AssertBlockedByAsync(refunding, ProcessId(writer), ProcessId(finalizer));
        }
        finally
        {
            await transaction.RollbackAsync();
            await refunding.WaitAsync(Timeout);
        }
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.CreditNoteRefunds.ToListAsync());
        Assert.Empty(await verify.PaymentSettlements.ToListAsync());
    }

    [Fact]
    public async Task Cash_refund_still_creates_one_linked_cash_out_and_replay_is_idempotent()
    {
        var tenant = await TenantAsync("cash-refund", 224);
        var saleId = await CreateSaleAsync(tenant, SalePaymentMethod.Cash);
        var noteId = await AddAuthorizedNoteAsync(tenant, saleId);
        await using var services = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(BeforeMidnight));
        var request = new RefundCreditNoteDto { Method = SalePaymentMethod.Cash };
        await services.Refunds.RefundAsync(noteId, request);
        await services.Refunds.RefundAsync(noteId, request);
        await using var verify = database.CreateDbContext();
        var refund = await verify.CreditNoteRefunds.SingleAsync();
        var movement = await verify.CashMovements.SingleAsync();
        Assert.Equal(CashMovementType.CashOut, movement.Type);
        Assert.Equal(4m, movement.Amount);
        Assert.Equal(movement.Id, refund.CashMovementId);
        Assert.Equal(movement.CashSessionId, refund.CashSessionId);
        var session = await verify.CashSessions.SingleAsync();
        Assert.Equal(26m, session.ExpectedCashAmount);
    }

    [Fact]
    public async Task Finalization_requires_transaction_and_revalidates_operational_context()
    {
        var tenant = await TenantAsync("guard", 225);
        await using var context = database.CreateDbContext();
        var guard = new TenantAdministrationGuard(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.LockPaymentSettlementFinalizationAsync(tenant.OperationalContext));
        var company = await context.Companies.SingleAsync();
        company.IsActive = false;
        await context.SaveChangesAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<OperationalContextException>(() =>
            guard.LockPaymentSettlementFinalizationAsync(tenant.OperationalContext));
        Assert.Equal("CONTEXT_MISMATCH", error.ErrorCode);
    }

    private async Task<TestTenant> TenantAsync(string key, int seed)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, 5m);
        await using var services = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(BeforeMidnight));
        await services.CashSessions.OpenAsync(new OpenCashSessionDto { OpeningAmount = 20m });
        return tenant;
    }

    private async Task<int> CreateSaleAsync(TestTenant tenant, SalePaymentMethod method)
    {
        await using var services = new TestServiceScope(database, tenant.OperationalContext,
            new FixedBusinessClock(BeforeMidnight));
        return (await services.Sales.CreateAsync(SaleRequest(tenant, method))).Id;
    }

    private async Task<int> AddAuthorizedNoteAsync(TestTenant tenant, int saleId)
    {
        await using var context = database.CreateDbContext();
        var note = new CreditNote
        {
            CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId, UserId = tenant.UserId,
            OriginalSaleId = saleId, Reason = "Synthetic refund", Total = 4m,
            DocumentStatus = SaleDocumentStatus.Authorized,
            AccessKey = new string('1', 49), AuthorizationNumber = "synthetic-test-only",
            BusinessDate = Day, TimeZoneIdSnapshot = "America/Guayaquil", CreatedAt = BeforeMidnight
        };
        context.CreditNotes.Add(note);
        await context.SaveChangesAsync();
        return note.Id;
    }

    private static SaleCreateDto SaleRequest(TestTenant tenant, SalePaymentMethod method) => new()
    {
        PaymentMethod = method, DocumentType = SaleDocumentType.Ticket,
        Items = [new SaleItemCreateDto { ProductId = tenant.Products[0].Id, Quantity = 1m,
            UnitPrice = tenant.Products[0].Price }]
    };

    private static PaymentSettlementCreateDto SettlementRequest(SalePaymentMethod method) => new()
    {
        RequestId = Guid.NewGuid(), BusinessDate = Day, PaymentMethod = method, SettledAmount = 0m
    };

    private static int ProcessId(TestServiceScope services)
        => ((NpgsqlConnection)services.DbContext.Database.GetDbConnection()).ProcessID;

    private async Task AssertBlockedByAsync(Task operation, int waiter, int blocker)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT pg_blocking_pids(@waiter)", observer);
        command.Parameters.AddWithValue("waiter", waiter);
        while (true)
        {
            // Observe a real PostgreSQL wait, not elapsed time or scheduler luck.
            var blockers = (int[])(await command.ExecuteScalarAsync(timeout.Token))!;
            if (blockers.Contains(blocker))
            {
                Assert.False(operation.IsCompleted);
                return;
            }
            Assert.False(operation.IsCompleted, "Operation completed without waiting for the Company barrier.");
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class UncommittedSaveGate(Func<DbContext, bool> matches) : SaveChangesInterceptor
    {
        public AsyncTestSignal Saved { get; } = new();
        public AsyncTestSignal Release { get; } = new();
        private bool _pauseThisSave;
        private bool _used;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _pauseThisSave = !_used && eventData.Context is not null && matches(eventData.Context);
            if (_pauseThisSave) _used = true;
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pauseThisSave)
            {
                Saved.Set();
                await Release.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
