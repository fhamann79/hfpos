using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PaymentSettlementIntegrationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateOnly Monday = new(2026, 9, 14);
    private static readonly DateOnly Tuesday = new(2026, 9, 15);
    private static readonly DateTime At = new(2026, 9, 15, 16, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_overview_has_four_zero_methods_and_today_is_preview_only()
    {
        var tenant = await TenantAsync("empty", 201);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var empty = await services.Settlements.GetReconciliationAsync(null);
        Assert.Equal(new DateOnly(2026, 9, 17), empty.BusinessDate);
        Assert.Equal(new DateOnly(2026, 9, 18), empty.CurrentBusinessDate);
        Assert.Equal(4, empty.Methods.Count);
        Assert.All(empty.Methods, m => Assert.Equal(0m, m.NetPaymentAmount));
        Assert.False(empty.Methods[0].CanSettle);
        Assert.All(empty.Methods.Skip(1), m => Assert.True(m.CanSettle));
        var today = await services.Settlements.GetReconciliationAsync(empty.CurrentBusinessDate);
        Assert.All(today.Methods, m => Assert.False(m.CanSettle));
    }

    [Fact]
    public async Task Gross_void_and_legacy_follow_event_dates_without_rewriting_original_day()
    {
        var tenant = await TenantAsync("events", 202);
        await AddSaleAsync(tenant, SalePaymentMethod.Cash, 10m, Monday);
        await AddSaleAsync(tenant, SalePaymentMethod.Transfer, 30m, Monday);
        await AddSaleAsync(tenant, SalePaymentMethod.Other, 40m, Monday);
        await AddSaleAsync(tenant, SalePaymentMethod.Card, 20m, Monday,
            SaleStatus.Voided, Tuesday);
        await AddSaleAsync(tenant, SalePaymentMethod.Card, 5m, Monday,
            SaleStatus.Voided, Monday);
        await AddSaleAsync(tenant, SalePaymentMethod.Other, 3m, Monday,
            SaleStatus.Voided, null);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var monday = await services.Settlements.GetReconciliationAsync(Monday);
        Assert.Equal(10m, monday.Methods[0].GrossSalesAmount);
        Assert.Equal(25m, monday.Methods[1].GrossSalesAmount);
        Assert.Equal(5m, monday.Methods[1].VoidAmount);
        Assert.Equal(20m, monday.Methods[1].NetPaymentAmount);
        Assert.Equal(30m, monday.Methods[2].GrossSalesAmount);
        Assert.Equal(43m, monday.Methods[3].GrossSalesAmount);
        Assert.Equal(1, monday.LegacyUnattributedVoidCount);
        var tuesday = await services.Settlements.GetReconciliationAsync(Tuesday);
        Assert.Equal(0m, tuesday.Methods[1].GrossSalesAmount);
        Assert.Equal(20m, tuesday.Methods[1].VoidAmount);
        Assert.Equal(-20m, tuesday.Methods[1].NetPaymentAmount);
        Assert.Equal(0m, tuesday.Methods[3].VoidAmount);
    }

    [Fact]
    public async Task Only_financial_refunds_reduce_cash_card_transfer_and_other_activity()
    {
        var tenant = await TenantAsync("refunds", 203);
        var sessionId = await OpenSessionAsync(tenant);
        var cashSale = await AddSaleAsync(tenant, SalePaymentMethod.Cash, 40m, Monday, cashSessionId: sessionId);
        var cardSale = await AddSaleAsync(tenant, SalePaymentMethod.Card, 40m, Monday);
        var transferSale = await AddSaleAsync(tenant, SalePaymentMethod.Transfer, 40m, Monday);
        var otherSale = await AddSaleAsync(tenant, SalePaymentMethod.Other, 40m, Monday);
        await AddCreditNoteAsync(tenant, cardSale, authorized: true);
        await AddRefundAsync(tenant, cashSale, SalePaymentMethod.Cash, 5m, sessionId);
        await AddRefundAsync(tenant, transferSale, SalePaymentMethod.Transfer, 7m);
        await AddRefundAsync(tenant, otherSale, SalePaymentMethod.Other, 9m);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var overview = await services.Settlements.GetReconciliationAsync(Tuesday);
        Assert.Equal(5m, overview.Methods[0].RefundAmount);
        Assert.Equal(-5m, overview.Methods[0].NetPaymentAmount);
        Assert.Equal(0m, overview.Methods[1].RefundAmount);
        Assert.Equal(7m, overview.Methods[2].RefundAmount);
        Assert.Equal(9m, overview.Methods[3].RefundAmount);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.CashMovements.CountAsync());
        Assert.Equal(5m, await verify.CashMovements.SumAsync(m => m.Amount));
    }

    [Fact]
    public async Task Cash_breakdown_classifies_manual_void_and_refund_without_double_counting()
    {
        var tenant = await TenantAsync("cash-breakdown", 204);
        var sessionId = await OpenSessionAsync(tenant);
        await AddSaleAsync(tenant, SalePaymentMethod.Cash, 10m, Monday, cashSessionId: sessionId);
        var voided = await AddSaleAsync(tenant, SalePaymentMethod.Cash, 4m, Monday,
            SaleStatus.Voided, Tuesday, sessionId, SaleVoidCashEffect.OriginalOpenSessionRecalculated, sessionId);
        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
        {
            await services.CashSessions.AddMovementAsync(sessionId,
                new CreateCashMovementDto { Type = CashMovementType.CashIn, Amount = 3m, Reason = "Manual in" });
            await services.CashSessions.AddMovementAsync(sessionId,
                new CreateCashMovementDto { Type = CashMovementType.CashOut, Amount = 2m, Reason = "Manual out" });
        }
        var postCloseSale = await AddSaleAsync(tenant, SalePaymentMethod.Cash, 8m, Monday);
        await AddLinkedCashOutAsync(tenant, sessionId, postCloseSale, 8m, true);
        await AddRefundAsync(tenant, voided, SalePaymentMethod.Cash, 1m, sessionId);
        await using var read = new TestServiceScope(database, tenant.OperationalContext);
        var session = (await read.CashSessions.GetByIdAsync(sessionId))!;
        var part = session.Reconciliation!;
        Assert.Equal(14m, part.GrossCashSalesAmount);
        Assert.Equal(4m, part.InSessionVoidAmount);
        Assert.Equal(10m, part.NetCashSalesAmount);
        Assert.Equal(3m, part.ManualCashInAmount);
        Assert.Equal(2m, part.ManualCashOutAmount);
        Assert.Equal(8m, part.SaleVoidCashOutAmount);
        Assert.Equal(1m, part.CreditNoteRefundCashOutAmount);
        Assert.Equal(22m, session.ExpectedCashAmount);
        Assert.Equal(session.ExpectedCashAmount, session.OpeningAmount + part.NetCashSalesAmount
            + part.ManualCashInAmount - part.ManualCashOutAmount
            - part.SaleVoidCashOutAmount - part.CreditNoteRefundCashOutAmount);
    }

    [Fact]
    public async Task Closed_cash_snapshot_stays_unchanged_after_post_close_void()
    {
        var tenant = await TenantAsync("closed", 205);
        var originalId = await OpenSessionAsync(tenant);
        var saleId = await AddSaleAsync(tenant, SalePaymentMethod.Cash, 10m, Monday, cashSessionId: originalId);
        await using (var services = new TestServiceScope(database, tenant.OperationalContext))
            await services.CashSessions.CloseAsync(originalId,
                new CloseCashSessionDto { CountedCashAmount = 30m });
        var currentId = await OpenSessionAsync(tenant);
        await AddLinkedCashOutAsync(tenant, currentId, saleId, 10m, true);
        await using var read = new TestServiceScope(database, tenant.OperationalContext);
        var original = (await read.CashSessions.GetByIdAsync(originalId))!;
        var current = (await read.CashSessions.GetByIdAsync(currentId))!;
        Assert.Equal(30m, original.ExpectedCashAmount);
        Assert.Equal(30m, original.CountedCashAmount);
        Assert.Equal(0m, original.DifferenceAmount);
        Assert.Equal(10m, original.Reconciliation!.NetCashSalesAmount);
        Assert.Equal(10m, current.Reconciliation!.SaleVoidCashOutAmount);
        Assert.Equal(10m, current.ExpectedCashAmount);
    }

    [Fact]
    public async Task Legacy_in_session_void_does_not_fabricate_attribution_and_flags_incomplete_breakdown()
    {
        var tenant = await TenantAsync("legacy-cash", 213);
        var sessionId = await OpenSessionAsync(tenant);
        await AddSaleAsync(tenant, SalePaymentMethod.Cash, 10m, Monday,
            SaleStatus.Voided, cashSessionId: sessionId);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var session = (await services.CashSessions.GetByIdAsync(sessionId))!;
        Assert.Equal(20m, session.ExpectedCashAmount);
        Assert.Equal(10m, session.Reconciliation!.GrossCashSalesAmount);
        Assert.Equal(0m, session.Reconciliation.InSessionVoidAmount);
        Assert.False(session.Reconciliation.IsReconstructionComplete);
    }

    [Fact]
    public async Task Settlement_snapshots_signed_amount_and_sequential_idempotence()
    {
        var tenant = await TenantAsync("persist", 206);
        var sale = await AddSaleAsync(tenant, SalePaymentMethod.Card, 20m, Monday);
        await AddRefundAsync(tenant, sale, SalePaymentMethod.Card, 30m);
        var request = Request(Tuesday, SalePaymentMethod.Card, -12m);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.Settlements.CreateAsync(request);
        Assert.Equal(-30m, first.ExpectedNetAmount);
        Assert.Equal(-12m, first.SettledAmount);
        Assert.Equal(18m, first.DifferenceAmount);
        var replay = await services.Settlements.CreateAsync(request);
        Assert.Equal(first.Id, replay.Id);
        Assert.True(replay.WasAlreadyProcessed);
        var changed = new PaymentSettlementCreateDto
        {
            RequestId = request.RequestId, BusinessDate = request.BusinessDate,
            PaymentMethod = request.PaymentMethod, SettledAmount = 1m,
            Reference = request.Reference, Notes = request.Notes
        };
        Assert.Equal("PAYMENT_SETTLEMENT_REQUEST_CONFLICT",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Settlements.CreateAsync(changed))).Message);
        Assert.Equal("PAYMENT_SETTLEMENT_ALREADY_RECONCILED",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Settlements.CreateAsync(
                Request(Tuesday, SalePaymentMethod.Card, -30m)))).Message);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.PaymentSettlements.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_same_request_is_idempotent_and_distinct_requests_conflict()
    {
        var tenant = await TenantAsync("race", 207);
        var request = Request(Monday, SalePaymentMethod.Transfer, 0m);
        await using var one = new TestServiceScope(database, tenant.OperationalContext);
        await using var two = new TestServiceScope(database, tenant.OperationalContext);
        var outcomes = await Task.WhenAll(one.Settlements.CreateAsync(request), two.Settlements.CreateAsync(request))
            .WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Equal(outcomes[0].Id, outcomes[1].Id);
        Assert.Single(outcomes.Where(s => s.WasAlreadyProcessed));
        await using var three = new TestServiceScope(database, tenant.OperationalContext);
        await using var four = new TestServiceScope(database, tenant.OperationalContext);
        var a = Capture(three.Settlements.CreateAsync(Request(Monday, SalePaymentMethod.Other, 2m)));
        var b = Capture(four.Settlements.CreateAsync(Request(Monday, SalePaymentMethod.Other, 3m)));
        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Single(results.Where(r => r.Value is not null));
        Assert.Single(results.Where(r => r.Error?.Message == "PAYMENT_SETTLEMENT_ALREADY_RECONCILED"));
        await using var verify = database.CreateDbContext();
        Assert.Equal(2, await verify.PaymentSettlements.CountAsync());
    }

    [Fact]
    public async Task Cash_current_date_and_invalid_amount_are_rejected_without_rows()
    {
        var tenant = await TenantAsync("reject", 208);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var cases = new[]
        {
            (Request(Monday, SalePaymentMethod.Cash, 1m), "PAYMENT_SETTLEMENT_METHOD_INVALID"),
            (Request(new DateOnly(2026, 9, 18), SalePaymentMethod.Card, 1m), "PAYMENT_SETTLEMENT_DATE_NOT_FINAL"),
            (Request(new DateOnly(2026, 9, 19), SalePaymentMethod.Card, 1m), "PAYMENT_SETTLEMENT_DATE_NOT_FINAL"),
            (Request(Monday, SalePaymentMethod.Card, 0.001m), "PAYMENT_SETTLEMENT_AMOUNT_INVALID"),
            (Request(Monday, SalePaymentMethod.Card, decimal.MinValue), "PAYMENT_SETTLEMENT_AMOUNT_INVALID")
        };
        foreach (var (request, code) in cases)
            Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(
                () => services.Settlements.CreateAsync(request))).Message);
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.PaymentSettlements.ToListAsync());
    }

    [Fact]
    public async Task History_and_detail_are_scoped_and_paginated_before_count()
    {
        var tenant = await TenantAsync("scope", 209);
        var foreign = await TenantAsync("foreign", 210);
        await using var own = new TestServiceScope(database, tenant.OperationalContext);
        var one = await own.Settlements.CreateAsync(Request(Monday, SalePaymentMethod.Card, 1m));
        await own.Settlements.CreateAsync(Request(Tuesday, SalePaymentMethod.Transfer, 2m));
        await using var other = new TestServiceScope(database, foreign.OperationalContext);
        var foreignRow = await other.Settlements.CreateAsync(Request(Monday, SalePaymentMethod.Other, 3m));
        Assert.Null(await own.Settlements.GetByIdAsync(foreignRow.Id));
        Assert.Null(await other.Settlements.GetByIdAsync(one.Id));
        var filtered = await own.Settlements.GetAsync(new PaymentSettlementQueryDto
        {
            From = Monday, To = Monday, PaymentMethod = SalePaymentMethod.Card, PageSize = 1
        });
        Assert.Equal(1, filtered.TotalItems);
        Assert.Equal(one.Id, Assert.Single(filtered.Items).Id);
        var secondPage = await own.Settlements.GetAsync(new PaymentSettlementQueryDto { Page = 2, PageSize = 1 });
        Assert.Equal(2, secondPage.TotalItems);
        Assert.Equal(one.Id, Assert.Single(secondPage.Items).Id);
        var otherContext = new OperationalContext
        {
            CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId + 1000,
            UserId = tenant.UserId,
            Username = tenant.OperationalContext.Username,
            CompanyTimeZoneId = tenant.OperationalContext.CompanyTimeZoneId
        };
        await using var point = new TestServiceScope(database, otherContext);
        Assert.Null(await point.Settlements.GetByIdAsync(one.Id));
        Assert.Empty((await point.Settlements.GetAsync(new PaymentSettlementQueryDto())).Items);
        var otherEstablishment = new OperationalContext
        {
            CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId + 1000,
            EmissionPointId = tenant.EmissionPointId,
            UserId = tenant.UserId,
            Username = tenant.OperationalContext.Username,
            CompanyTimeZoneId = tenant.OperationalContext.CompanyTimeZoneId
        };
        await using var establishment = new TestServiceScope(database, otherEstablishment);
        Assert.Null(await establishment.Settlements.GetByIdAsync(one.Id));
        Assert.Empty((await establishment.Settlements.GetAsync(new PaymentSettlementQueryDto())).Items);
    }

    [Fact]
    public async Task Save_failure_rolls_back_without_persisting_a_settlement()
    {
        var tenant = await TenantAsync("rollback", 211);
        await using (var services = new TestServiceScope(database, tenant.OperationalContext,
            new FailSettlementSaveInterceptor()))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                services.Settlements.CreateAsync(Request(Monday, SalePaymentMethod.Card, 1m)));
            Assert.Equal("Synthetic save failure", error.Message);
        }
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.PaymentSettlements.ToListAsync());
    }

    [Fact]
    public async Task Request_id_from_another_emission_point_is_never_returned_or_reused()
    {
        var tenant = await TenantAsync("cross-point", 212);
        var request = Request(Monday, SalePaymentMethod.Card, 5m);
        int foreignId;
        await using (var context = database.CreateDbContext())
        {
            var point = new EmissionPoint
            {
                EstablishmentId = tenant.EstablishmentId, Code = "002", Name = "Other point",
                IsActive = true, CreatedAt = At
            };
            context.EmissionPoints.Add(point);
            await context.SaveChangesAsync();
            var foreign = new PaymentSettlement
            {
                CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = point.Id, ReconciledByUserId = tenant.UserId,
                BusinessDate = Monday, PaymentMethod = SalePaymentMethod.Card,
                GrossSalesAmount = 0, VoidAmount = 0, RefundAmount = 0,
                ExpectedNetAmount = 0, SettledAmount = 5, DifferenceAmount = 5,
                RequestId = request.RequestId, Reference = request.Reference,
                ReconciledAt = At, TimeZoneIdSnapshot = "America/Guayaquil"
            };
            context.PaymentSettlements.Add(foreign);
            await context.SaveChangesAsync();
            foreignId = foreign.Id;
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        Assert.Null(await services.Settlements.GetByIdAsync(foreignId));
        Assert.Equal("PAYMENT_SETTLEMENT_REQUEST_CONFLICT",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => services.Settlements.CreateAsync(request))).Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.PaymentSettlements.CountAsync());
    }

    private async Task<TestTenant> TenantAsync(string key, int seed) =>
        await TestDataBuilder.CreateTenantAsync(database, key, seed, 10m);

    private static PaymentSettlementCreateDto Request(DateOnly date, SalePaymentMethod method, decimal amount) => new()
    {
        RequestId = Guid.NewGuid(), BusinessDate = date, PaymentMethod = method, SettledAmount = amount,
        Reference = "Synthetic", Notes = null
    };

    private async Task<int> OpenSessionAsync(TestTenant tenant)
    {
        await using var service = new TestServiceScope(database, tenant.OperationalContext);
        return (await service.CashSessions.OpenAsync(new OpenCashSessionDto { OpeningAmount = 20m })).Id;
    }

    private async Task<int> AddSaleAsync(TestTenant tenant, SalePaymentMethod method, decimal amount,
        DateOnly businessDate, SaleStatus status = SaleStatus.Completed, DateOnly? voidDate = null,
        int? cashSessionId = null, SaleVoidCashEffect? effect = null, int? voidSessionId = null)
    {
        await using var context = database.CreateDbContext();
        var sale = new Sale
        {
            CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId, UserId = tenant.UserId,
            Status = status, PaymentMethod = method, DocumentType = SaleDocumentType.Ticket,
            DocumentStatus = SaleDocumentStatus.NotRequired, CashSessionId = cashSessionId,
            Total = amount, Subtotal = amount, GrossSubtotal = amount,
            BusinessDate = businessDate, TimeZoneIdSnapshot = "America/Guayaquil", CreatedAt = At,
            VoidedAt = status == SaleStatus.Voided && voidDate.HasValue ? At : null,
            VoidedByUserId = status == SaleStatus.Voided && voidDate.HasValue ? tenant.UserId : null,
            VoidReason = status == SaleStatus.Voided && voidDate.HasValue ? "Synthetic void" : null,
            VoidBusinessDate = voidDate,
            VoidTimeZoneIdSnapshot = voidDate.HasValue ? "America/Guayaquil" : null,
            VoidCashEffect = effect ?? (voidDate.HasValue ? SaleVoidCashEffect.NoCashMovement : null),
            VoidCashSessionId = voidSessionId
        };
        context.Sales.Add(sale);
        await context.SaveChangesAsync();
        return sale.Id;
    }

    private async Task<int> AddCreditNoteAsync(TestTenant tenant, int saleId, bool authorized = false)
    {
        await using var context = database.CreateDbContext();
        var note = new CreditNote
        {
            CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId, UserId = tenant.UserId,
            OriginalSaleId = saleId, Reason = "Synthetic refund", Total = 10m,
            DocumentStatus = authorized ? SaleDocumentStatus.Authorized : SaleDocumentStatus.NotRequired,
            BusinessDate = Tuesday, TimeZoneIdSnapshot = "America/Guayaquil", CreatedAt = At
        };
        context.CreditNotes.Add(note);
        await context.SaveChangesAsync();
        return note.Id;
    }

    private async Task AddRefundAsync(TestTenant tenant, int saleId, SalePaymentMethod method,
        decimal amount, int? cashSessionId = null)
    {
        var noteId = await AddCreditNoteAsync(tenant, saleId);
        await using var context = database.CreateDbContext();
        int? movementId = null;
        if (method == SalePaymentMethod.Cash)
        {
            var movement = NewCashOut(tenant, cashSessionId!.Value, amount);
            context.CashMovements.Add(movement);
            await context.SaveChangesAsync();
            movementId = movement.Id;
        }
        context.CreditNoteRefunds.Add(new CreditNoteRefund
        {
            CreditNoteId = noteId, CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId, EmissionPointId = tenant.EmissionPointId,
            RefundedByUserId = tenant.UserId, Method = method, Amount = amount,
            RefundedAt = At, BusinessDate = Tuesday, TimeZoneIdSnapshot = "America/Guayaquil",
            CashSessionId = cashSessionId, CashMovementId = movementId
        });
        await context.SaveChangesAsync();
    }

    private async Task AddLinkedCashOutAsync(TestTenant tenant, int sessionId, int saleId,
        decimal amount, bool postClose)
    {
        await using var context = database.CreateDbContext();
        var movement = NewCashOut(tenant, sessionId, amount);
        context.CashMovements.Add(movement);
        await context.SaveChangesAsync();
        var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = At;
        sale.VoidedByUserId = tenant.UserId;
        sale.VoidReason = "Synthetic void";
        sale.VoidBusinessDate = Tuesday;
        sale.VoidTimeZoneIdSnapshot = "America/Guayaquil";
        sale.VoidCashEffect = postClose ? SaleVoidCashEffect.CurrentSessionCashOut : SaleVoidCashEffect.OriginalOpenSessionRecalculated;
        sale.VoidCashSessionId = sessionId;
        sale.VoidCashMovementId = movement.Id;
        await context.SaveChangesAsync();
    }

    private static CashMovement NewCashOut(TestTenant tenant, int sessionId, decimal amount) => new()
    {
        CashSessionId = sessionId, CompanyId = tenant.CompanyId,
        EstablishmentId = tenant.EstablishmentId, EmissionPointId = tenant.EmissionPointId,
        UserId = tenant.UserId, Type = CashMovementType.CashOut, Amount = amount,
        Reason = "Synthetic automatic movement", CreatedAt = At, BusinessDate = Tuesday,
        TimeZoneIdSnapshot = "America/Guayaquil"
    };

    private static async Task<(PaymentSettlementDto? Value, Exception? Error)> Capture(Task<PaymentSettlementDto> task)
    {
        try { return (await task, null); }
        catch (Exception ex) { return (null, ex); }
    }

    private sealed class FailSettlementSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<PaymentSettlement>()
                .Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("Synthetic save failure");
            return ValueTask.FromResult(result);
        }
    }
}
