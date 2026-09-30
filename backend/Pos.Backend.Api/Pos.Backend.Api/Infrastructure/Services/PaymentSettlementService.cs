using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PaymentSettlementService(
    PosDbContext context,
    IOperationalContextAccessor contextAccessor,
    IBusinessClockService clock,
    TenantAdministrationGuard administrationGuard) : IPaymentSettlementService
{
    public async Task<PaymentReconciliationDto> GetReconciliationAsync(DateOnly? businessDate)
    {
        if (businessDate == default(DateOnly))
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_DATE_INVALID");

        var scope = await contextAccessor.GetRequiredContextAsync();
        var nowDate = clock.GetBusinessDate(clock.UtcNow, scope.CompanyTimeZoneId);
        var date = businessDate ?? nowDate.AddDays(-1);
        var activities = await CalculateActivityAsync(scope, date);
        var settlements = await ScopedSettlements(scope)
            .Where(s => s.BusinessDate == date)
            .Select(Projection).ToListAsync();
        var byMethod = settlements.ToDictionary(s => s.PaymentMethod);
        var legacyCount = await ScopedSales(scope)
            .CountAsync(s => s.Status == SaleStatus.Voided && s.VoidBusinessDate == null);

        return new PaymentReconciliationDto
        {
            BusinessDate = date,
            CurrentBusinessDate = nowDate,
            LegacyUnattributedVoidCount = legacyCount,
            Methods = Enum.GetValues<SalePaymentMethod>()
                .Select(method =>
                {
                    var amounts = activities.GetValueOrDefault(method);
                    byMethod.TryGetValue(method, out var settlement);
                    return new PaymentMethodActivityDto
                    {
                        PaymentMethod = method,
                        GrossSalesAmount = amounts.Gross,
                        VoidAmount = amounts.Void,
                        RefundAmount = amounts.Refund,
                        NetPaymentAmount = Round(amounts.Gross - amounts.Void - amounts.Refund),
                        CanSettle = method != SalePaymentMethod.Cash && date < nowDate && settlement is null,
                        Settlement = settlement
                    };
                }).ToArray()
        };
    }

    public async Task<PaymentSettlementDto> CreateAsync(PaymentSettlementCreateDto request)
    {
        Validate(request);
        var scope = await contextAccessor.GetRequiredContextAsync();
        var reference = Normalize(request.Reference);
        var notes = Normalize(request.Notes);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await administrationGuard.LockPaymentSettlementFinalizationAsync(scope);

            var prior = await FindByRequestIdAsync(scope, request.RequestId);
            if (prior is not null)
            {
                EnsureSameRequest(prior, request, reference, notes);
                prior.WasAlreadyProcessed = true;
                return prior;
            }

            var currentDate = clock.GetBusinessDate(clock.UtcNow, scope.CompanyTimeZoneId);
            if (request.BusinessDate >= currentDate)
                throw new InvalidOperationException("PAYMENT_SETTLEMENT_DATE_NOT_FINAL");

            if (await ScopedSettlements(scope).AnyAsync(s => s.BusinessDate == request.BusinessDate
                && s.PaymentMethod == request.PaymentMethod))
            {
                // READ COMMITTED can observe the winning commit between the two preflight reads.
                prior = await FindByRequestIdAsync(scope, request.RequestId);
                if (prior is not null)
                {
                    EnsureSameRequest(prior, request, reference, notes);
                    prior.WasAlreadyProcessed = true;
                    return prior;
                }
                throw new InvalidOperationException("PAYMENT_SETTLEMENT_ALREADY_RECONCILED");
            }

            var amounts = (await CalculateActivityAsync(scope, request.BusinessDate))[request.PaymentMethod];
            var expected = Round(amounts.Gross - amounts.Void - amounts.Refund);
            var settlement = new PaymentSettlement
            {
                CompanyId = scope.CompanyId,
                EstablishmentId = scope.EstablishmentId,
                EmissionPointId = scope.EmissionPointId,
                BusinessDate = request.BusinessDate,
                PaymentMethod = request.PaymentMethod,
                GrossSalesAmount = amounts.Gross,
                VoidAmount = amounts.Void,
                RefundAmount = amounts.Refund,
                ExpectedNetAmount = expected,
                SettledAmount = request.SettledAmount,
                DifferenceAmount = Round(request.SettledAmount - expected),
                RequestId = request.RequestId,
                Reference = reference,
                Notes = notes,
                ReconciledByUserId = scope.UserId,
                ReconciledAt = clock.UtcNow,
                TimeZoneIdSnapshot = scope.CompanyTimeZoneId
            };
            context.PaymentSettlements.Add(settlement);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return (await GetByIdAsync(settlement.Id))!;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName is "IX_PaymentSettlements_CompanyId_RequestId"
                or "UX_PaymentSettlements_ContextDateMethod")
        {
            context.ChangeTracker.Clear();
            var prior = await FindByRequestIdAsync(scope, request.RequestId);
            if (prior is not null)
            {
                EnsureSameRequest(prior, request, reference, notes);
                prior.WasAlreadyProcessed = true;
                return prior;
            }

            throw new InvalidOperationException(
                postgres.ConstraintName == "IX_PaymentSettlements_CompanyId_RequestId"
                    ? "PAYMENT_SETTLEMENT_REQUEST_CONFLICT"
                    : "PAYMENT_SETTLEMENT_ALREADY_RECONCILED", ex);
        }
    }

    public async Task<PagedResultDto<PaymentSettlementDto>> GetAsync(PaymentSettlementQueryDto query)
    {
        var scope = await contextAccessor.GetRequiredContextAsync();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var rows = ScopedSettlements(scope);
        if (query.From.HasValue)
            rows = rows.Where(s => s.BusinessDate >= query.From.Value);
        if (query.To.HasValue)
            rows = rows.Where(s => s.BusinessDate <= query.To.Value);
        if (query.PaymentMethod.HasValue)
            rows = rows.Where(s => s.PaymentMethod == query.PaymentMethod.Value);

        var total = await rows.CountAsync();
        var items = await rows.OrderByDescending(s => s.BusinessDate)
            .ThenByDescending(s => s.ReconciledAt).ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(Projection).ToListAsync();
        return new PagedResultDto<PaymentSettlementDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<PaymentSettlementDto?> GetByIdAsync(int id)
    {
        var scope = await contextAccessor.GetRequiredContextAsync();
        return await ScopedSettlements(scope).Where(s => s.Id == id)
            .Select(Projection).SingleOrDefaultAsync();
    }

    private IQueryable<Sale> ScopedSales(OperationalContext scope) =>
        context.Sales.AsNoTracking().Where(s => s.CompanyId == scope.CompanyId
            && s.EstablishmentId == scope.EstablishmentId
            && s.EmissionPointId == scope.EmissionPointId);

    private IQueryable<PaymentSettlement> ScopedSettlements(OperationalContext scope) =>
        context.PaymentSettlements.AsNoTracking().Where(s => s.CompanyId == scope.CompanyId
            && s.EstablishmentId == scope.EstablishmentId
            && s.EmissionPointId == scope.EmissionPointId);

    private async Task<PaymentSettlementDto?> FindByRequestIdAsync(OperationalContext scope, Guid requestId) =>
        await ScopedSettlements(scope).Where(s => s.RequestId == requestId)
            .Select(Projection).SingleOrDefaultAsync();

    private async Task<Dictionary<SalePaymentMethod, ActivityAmounts>> CalculateActivityAsync(
        OperationalContext scope, DateOnly date)
    {
        var gross = await ScopedSales(scope)
            .Where(s => s.BusinessDate == date
                && (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Voided))
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new { Method = g.Key, Amount = g.Sum(s => s.Total) })
            .ToListAsync();
        var voids = await ScopedSales(scope)
            .Where(s => s.Status == SaleStatus.Voided && s.VoidBusinessDate == date
                && s.VoidedAt != null && s.VoidedByUserId != null && s.VoidReason != null
                && s.VoidTimeZoneIdSnapshot != null && s.VoidCashEffect != null)
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new { Method = g.Key, Amount = g.Sum(s => s.Total) })
            .ToListAsync();
        var refunds = await context.CreditNoteRefunds.AsNoTracking()
            .Where(r => r.CompanyId == scope.CompanyId
                && r.EstablishmentId == scope.EstablishmentId
                && r.EmissionPointId == scope.EmissionPointId
                && r.BusinessDate == date)
            .GroupBy(r => r.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(r => r.Amount) })
            .ToListAsync();

        var result = Enum.GetValues<SalePaymentMethod>()
            .ToDictionary(m => m, _ => new ActivityAmounts(0, 0, 0));
        foreach (var row in gross)
            result[row.Method] = result[row.Method] with { Gross = Round(row.Amount) };
        foreach (var row in voids)
            result[row.Method] = result[row.Method] with { Void = Round(row.Amount) };
        foreach (var row in refunds)
            result[row.Method] = result[row.Method] with { Refund = Round(row.Amount) };
        return result;
    }

    private static readonly Expression<Func<PaymentSettlement, PaymentSettlementDto>> Projection = s => new PaymentSettlementDto
    {
        Id = s.Id,
        BusinessDate = s.BusinessDate,
        PaymentMethod = s.PaymentMethod,
        GrossSalesAmount = s.GrossSalesAmount,
        VoidAmount = s.VoidAmount,
        RefundAmount = s.RefundAmount,
        ExpectedNetAmount = s.ExpectedNetAmount,
        SettledAmount = s.SettledAmount,
        DifferenceAmount = s.DifferenceAmount,
        RequestId = s.RequestId,
        Reference = s.Reference,
        Notes = s.Notes,
        ReconciledByUserId = s.ReconciledByUserId,
        ReconciledByUsername = s.ReconciledByUser.Username,
        ReconciledAt = s.ReconciledAt,
        TimeZoneIdSnapshot = s.TimeZoneIdSnapshot
    };

    private static void Validate(PaymentSettlementCreateDto request)
    {
        if (request.RequestId == Guid.Empty)
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_REQUEST_ID_REQUIRED");
        if (request.BusinessDate == default)
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_DATE_INVALID");
        if (request.PaymentMethod is not (SalePaymentMethod.Card or SalePaymentMethod.Transfer or SalePaymentMethod.Other))
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_METHOD_INVALID");
        if (decimal.Round(request.SettledAmount, 2) != request.SettledAmount
            || request.SettledAmount < -9999999999999999.99m
            || request.SettledAmount > 9999999999999999.99m)
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_AMOUNT_INVALID");
        if (Normalize(request.Reference)?.Length > 150 || Normalize(request.Notes)?.Length > 500)
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_TEXT_INVALID");
    }

    private static void EnsureSameRequest(PaymentSettlementDto prior, PaymentSettlementCreateDto request,
        string? reference, string? notes)
    {
        if (prior.BusinessDate != request.BusinessDate || prior.PaymentMethod != request.PaymentMethod
            || prior.SettledAmount != request.SettledAmount
            || prior.Reference != reference || prior.Notes != notes)
            throw new InvalidOperationException("PAYMENT_SETTLEMENT_REQUEST_CONFLICT");
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private readonly record struct ActivityAmounts(decimal Gross, decimal Void, decimal Refund);
}
