using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CashSessionService : ICashSessionService
{
    private readonly PosDbContext _context;
    private readonly ILogger<CashSessionService> _logger;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IBusinessClockService _businessClock;

    public CashSessionService(
        PosDbContext context,
        ILogger<CashSessionService> logger,
        IOperationalContextAccessor operationalContextAccessor,
        IBusinessClockService businessClock)
    {
        _context = context;
        _logger = logger;
        _operationalContextAccessor = operationalContextAccessor;
        _businessClock = businessClock;
    }

    public async Task<CashSessionDto?> GetCurrentAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var session = await BuildBaseSessionQuery(operationalContext)
            .AsNoTracking()
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .Include(s => s.Movements.OrderByDescending(m => m.CreatedAt))
                .ThenInclude(m => m.User)
            .Where(s => s.OpenedByUserId == operationalContext.UserId
                && s.Status == CashSessionStatus.Open)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync();

        return session is null ? null : await ToDtoAsync(session);
    }

    public async Task<IReadOnlyList<CashSessionListItemDto>> GetListAsync(
        DateTime? from,
        DateTime? to,
        CashSessionStatus? status,
        int? userId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        IQueryable<CashSession> query = BuildBaseSessionQuery(operationalContext)
            .AsNoTracking()
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser);

        if (from.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(from.Value);
            query = query.Where(s => s.OpenBusinessDate >= fromDate);
        }

        if (to.HasValue)
        {
            var toDate = DateOnly.FromDateTime(to.Value);
            query = query.Where(s => s.OpenBusinessDate <= toDate);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(s => s.OpenedByUserId == userId.Value);
        }

        var sessions = await query
            .OrderByDescending(s => s.OpenBusinessDate)
            .ThenByDescending(s => s.OpenedAt)
            .ThenByDescending(s => s.Id)
            .ToListAsync();

        var result = new List<CashSessionListItemDto>();
        foreach (var session in sessions)
        {
            result.Add(await ToListItemDtoAsync(session));
        }

        return result;
    }

    public async Task<CashSessionDto?> GetByIdAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var session = await BuildBaseSessionQuery(operationalContext)
            .AsNoTracking()
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .Include(s => s.Movements.OrderByDescending(m => m.CreatedAt))
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(s => s.Id == id);

        return session is null ? null : await ToDtoAsync(session);
    }

    public async Task<CashSessionDto> OpenAsync(OpenCashSessionDto dto)
    {
        if (dto.OpeningAmount < 0m)
        {
            throw new InvalidOperationException("CASH_SESSION_OPENING_AMOUNT_INVALID");
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var alreadyOpen = await _context.CashSessions.AnyAsync(s =>
            s.CompanyId == operationalContext.CompanyId
            && s.EstablishmentId == operationalContext.EstablishmentId
            && s.EmissionPointId == operationalContext.EmissionPointId
            && s.OpenedByUserId == operationalContext.UserId
            && s.Status == CashSessionStatus.Open);

        if (alreadyOpen)
        {
            throw new InvalidOperationException("CASH_SESSION_ALREADY_OPEN");
        }

        var now = _businessClock.UtcNow;
        var businessDate = _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId);
        var openingAmount = RoundMoney(dto.OpeningAmount);

        var session = new CashSession
        {
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = operationalContext.EstablishmentId,
            EmissionPointId = operationalContext.EmissionPointId,
            OpenedByUserId = operationalContext.UserId,
            Status = CashSessionStatus.Open,
            OpeningAmount = openingAmount,
            ExpectedCashAmount = openingAmount,
            OpenedAt = now,
            OpenBusinessDate = businessDate,
            OpenTimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId,
            OpeningNotes = NormalizeOptionalText(dto.OpeningNotes)
        };

        _context.CashSessions.Add(session);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintFailure(ex))
        {
            throw new InvalidOperationException("CASH_SESSION_ALREADY_OPEN", ex);
        }

        _logger.LogInformation(
            "Cash session opened. CashSessionId {CashSessionId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
            session.Id,
            operationalContext.UserId,
            operationalContext.CompanyId,
            operationalContext.EstablishmentId,
            operationalContext.EmissionPointId);

        var created = await GetByIdAsync(session.Id);
        return created ?? throw new KeyNotFoundException("CASH_SESSION_NOT_FOUND");
    }

    public async Task<CashSessionDto> AddMovementAsync(int id, CreateCashMovementDto dto)
    {
        if (!Enum.IsDefined(typeof(CashMovementType), dto.Type))
        {
            throw new InvalidOperationException("CASH_MOVEMENT_AMOUNT_INVALID");
        }

        if (dto.Amount <= 0m)
        {
            throw new InvalidOperationException("CASH_MOVEMENT_AMOUNT_INVALID");
        }

        var reason = NormalizeOptionalText(dto.Reason);
        if (reason is null || reason.Length > 300)
        {
            throw new InvalidOperationException("CASH_MOVEMENT_REASON_REQUIRED");
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var session = await GetLockedSessionAsync(id);
        EnsureSessionExistsAndMatchesContext(session, operationalContext, requireCurrentUser: true);

        if (session!.Status == CashSessionStatus.Closed)
        {
            throw new InvalidOperationException("CASH_SESSION_ALREADY_CLOSED");
        }

        if (session.Status != CashSessionStatus.Open)
        {
            throw new InvalidOperationException("CASH_SESSION_NOT_OPEN");
        }

        var now = _businessClock.UtcNow;
        var amount = RoundMoney(dto.Amount);
        var movement = new CashMovement
        {
            CashSessionId = session.Id,
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = operationalContext.EstablishmentId,
            EmissionPointId = operationalContext.EmissionPointId,
            UserId = operationalContext.UserId,
            Type = dto.Type,
            Amount = amount,
            Reason = reason,
            CreatedAt = now,
            BusinessDate = _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId),
            TimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId
        };

        if (dto.Type == CashMovementType.CashIn)
        {
            session.CashInAmount = RoundMoney(session.CashInAmount + amount);
            session.ExpectedCashAmount = RoundMoney(session.ExpectedCashAmount + amount);
        }
        else
        {
            session.CashOutAmount = RoundMoney(session.CashOutAmount + amount);
            session.ExpectedCashAmount = RoundMoney(session.ExpectedCashAmount - amount);
        }

        _context.CashMovements.Add(movement);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var updated = await GetByIdAsync(session.Id);
        return updated ?? throw new KeyNotFoundException("CASH_SESSION_NOT_FOUND");
    }

    public async Task<CashSessionDto> CloseAsync(int id, CloseCashSessionDto dto)
    {
        if (dto.CountedCashAmount < 0m)
        {
            throw new InvalidOperationException("CASH_SESSION_COUNTED_AMOUNT_INVALID");
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var session = await GetLockedSessionAsync(id);
        EnsureSessionExistsAndMatchesContext(session, operationalContext, requireCurrentUser: true);

        if (session!.Status == CashSessionStatus.Closed)
        {
            throw new InvalidOperationException("CASH_SESSION_ALREADY_CLOSED");
        }

        if (session.Status != CashSessionStatus.Open)
        {
            throw new InvalidOperationException("CASH_SESSION_NOT_OPEN");
        }

        var totals = await CalculateLiveTotalsAsync(session.Id, session.OpeningAmount);
        var countedAmount = RoundMoney(dto.CountedCashAmount);
        var now = _businessClock.UtcNow;

        session.CashSalesAmount = totals.CashSalesAmount;
        session.CardSalesAmount = totals.CardSalesAmount;
        session.TransferSalesAmount = totals.TransferSalesAmount;
        session.OtherSalesAmount = totals.OtherSalesAmount;
        session.CashInAmount = totals.CashInAmount;
        session.CashOutAmount = totals.CashOutAmount;
        session.ExpectedCashAmount = totals.ExpectedCashAmount;
        session.CountedCashAmount = countedAmount;
        session.DifferenceAmount = RoundMoney(countedAmount - totals.ExpectedCashAmount);
        session.ClosedAt = now;
        session.ClosedBusinessDate = _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId);
        session.ClosedTimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId;
        session.ClosedByUserId = operationalContext.UserId;
        session.ClosingNotes = NormalizeOptionalText(dto.ClosingNotes);
        session.Status = CashSessionStatus.Closed;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "Cash session closed. CashSessionId {CashSessionId} UserId {UserId} Expected {ExpectedCashAmount} Counted {CountedCashAmount} Difference {DifferenceAmount}",
            session.Id,
            operationalContext.UserId,
            session.ExpectedCashAmount,
            session.CountedCashAmount,
            session.DifferenceAmount);

        var updated = await GetByIdAsync(session.Id);
        return updated ?? throw new KeyNotFoundException("CASH_SESSION_NOT_FOUND");
    }

    public async Task<CashSession> GetRequiredOpenSessionForCurrentContextAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var session = await _context.CashSessions
            .AsNoTracking()
            .Where(s => s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId
                && s.OpenedByUserId == operationalContext.UserId
                && s.Status == CashSessionStatus.Open)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync();

        return session ?? throw new InvalidOperationException("CASH_SESSION_REQUIRED");
    }

    private IQueryable<CashSession> BuildBaseSessionQuery(OperationalContext operationalContext)
    {
        return _context.CashSessions
            .Where(s => s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);
    }

    private async Task<CashSession?> GetLockedSessionAsync(int id)
    {
        return await _context.CashSessions
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CashSessions""
                WHERE ""Id"" = {id}
                FOR UPDATE")
            .SingleOrDefaultAsync();
    }

    private static void EnsureSessionExistsAndMatchesContext(
        CashSession? session,
        OperationalContext operationalContext,
        bool requireCurrentUser)
    {
        if (session is null)
        {
            throw new KeyNotFoundException("CASH_SESSION_NOT_FOUND");
        }

        if (session.CompanyId != operationalContext.CompanyId
            || session.EstablishmentId != operationalContext.EstablishmentId
            || session.EmissionPointId != operationalContext.EmissionPointId
            || (requireCurrentUser && session.OpenedByUserId != operationalContext.UserId))
        {
            throw new InvalidOperationException("CASH_SESSION_CONTEXT_MISMATCH");
        }
    }

    private async Task<CashSessionTotals> CalculateDisplayTotalsAsync(CashSession session)
    {
        return session.Status == CashSessionStatus.Closed
            ? CashSessionTotals.FromPersisted(session)
            : await CalculateLiveTotalsAsync(session.Id, session.OpeningAmount);
    }

    private async Task<CashSessionTotals> CalculateLiveTotalsAsync(int cashSessionId, decimal openingAmount)
    {
        var saleRows = await _context.Sales
            .AsNoTracking()
            .Where(s => s.CashSessionId == cashSessionId && s.Status == SaleStatus.Completed)
            .GroupBy(s => s.PaymentMethod)
            .Select(g => new { PaymentMethod = g.Key, Amount = g.Sum(s => s.Total) })
            .ToListAsync();

        var movementRows = await _context.CashMovements
            .AsNoTracking()
            .Where(m => m.CashSessionId == cashSessionId)
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key, Amount = g.Sum(m => m.Amount) })
            .ToListAsync();

        var cashSales = RoundMoney(saleRows
            .Where(s => s.PaymentMethod == SalePaymentMethod.Cash)
            .Sum(s => s.Amount));

        var cardSales = RoundMoney(saleRows
            .Where(s => s.PaymentMethod == SalePaymentMethod.Card)
            .Sum(s => s.Amount));

        var transferSales = RoundMoney(saleRows
            .Where(s => s.PaymentMethod == SalePaymentMethod.Transfer)
            .Sum(s => s.Amount));

        var otherSales = RoundMoney(saleRows
            .Where(s => s.PaymentMethod == SalePaymentMethod.Other)
            .Sum(s => s.Amount));

        var cashIn = RoundMoney(movementRows
            .Where(m => m.Type == CashMovementType.CashIn)
            .Sum(m => m.Amount));

        var cashOut = RoundMoney(movementRows
            .Where(m => m.Type == CashMovementType.CashOut)
            .Sum(m => m.Amount));

        var expected = RoundMoney(openingAmount + cashSales + cashIn - cashOut);

        return new CashSessionTotals(
            cashSales,
            cardSales,
            transferSales,
            otherSales,
            cashIn,
            cashOut,
            expected);
    }

    private async Task<CashSessionDto> ToDtoAsync(CashSession session)
    {
        var totals = await CalculateDisplayTotalsAsync(session);

        return new CashSessionDto
        {
            Id = session.Id,
            CompanyId = session.CompanyId,
            EstablishmentId = session.EstablishmentId,
            EmissionPointId = session.EmissionPointId,
            OpenedByUserId = session.OpenedByUserId,
            OpenedByUsername = session.OpenedByUser.Username,
            ClosedByUserId = session.ClosedByUserId,
            ClosedByUsername = session.ClosedByUser?.Username,
            Status = session.Status,
            OpeningAmount = session.OpeningAmount,
            ExpectedCashAmount = totals.ExpectedCashAmount,
            CountedCashAmount = session.CountedCashAmount,
            DifferenceAmount = session.DifferenceAmount,
            CashSalesAmount = totals.CashSalesAmount,
            CardSalesAmount = totals.CardSalesAmount,
            TransferSalesAmount = totals.TransferSalesAmount,
            OtherSalesAmount = totals.OtherSalesAmount,
            CashInAmount = totals.CashInAmount,
            CashOutAmount = totals.CashOutAmount,
            OpenedAt = session.OpenedAt,
            OpenBusinessDate = session.OpenBusinessDate,
            OpenTimeZoneIdSnapshot = session.OpenTimeZoneIdSnapshot,
            ClosedAt = session.ClosedAt,
            ClosedBusinessDate = session.ClosedBusinessDate,
            ClosedTimeZoneIdSnapshot = session.ClosedTimeZoneIdSnapshot,
            OpeningNotes = session.OpeningNotes,
            ClosingNotes = session.ClosingNotes,
            Movements = session.Movements
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .Select(ToMovementDto)
                .ToList()
        };
    }

    private async Task<CashSessionListItemDto> ToListItemDtoAsync(CashSession session)
    {
        var totals = await CalculateDisplayTotalsAsync(session);

        return new CashSessionListItemDto
        {
            Id = session.Id,
            OpenedByUserId = session.OpenedByUserId,
            OpenedByUsername = session.OpenedByUser.Username,
            ClosedByUserId = session.ClosedByUserId,
            ClosedByUsername = session.ClosedByUser?.Username,
            Status = session.Status,
            OpeningAmount = session.OpeningAmount,
            ExpectedCashAmount = totals.ExpectedCashAmount,
            CountedCashAmount = session.CountedCashAmount,
            DifferenceAmount = session.DifferenceAmount,
            CashSalesAmount = totals.CashSalesAmount,
            CardSalesAmount = totals.CardSalesAmount,
            TransferSalesAmount = totals.TransferSalesAmount,
            OtherSalesAmount = totals.OtherSalesAmount,
            CashInAmount = totals.CashInAmount,
            CashOutAmount = totals.CashOutAmount,
            OpenedAt = session.OpenedAt,
            OpenBusinessDate = session.OpenBusinessDate,
            OpenTimeZoneIdSnapshot = session.OpenTimeZoneIdSnapshot,
            ClosedAt = session.ClosedAt,
            ClosedBusinessDate = session.ClosedBusinessDate,
            ClosedTimeZoneIdSnapshot = session.ClosedTimeZoneIdSnapshot,
            OpeningNotes = session.OpeningNotes,
            ClosingNotes = session.ClosingNotes
        };
    }

    private static CashMovementDto ToMovementDto(CashMovement movement)
    {
        return new CashMovementDto
        {
            Id = movement.Id,
            CashSessionId = movement.CashSessionId,
            Type = movement.Type,
            Amount = movement.Amount,
            Reason = movement.Reason,
            UserId = movement.UserId,
            Username = movement.User.Username,
            CreatedAt = movement.CreatedAt,
            BusinessDate = movement.BusinessDate,
            TimeZoneIdSnapshot = movement.TimeZoneIdSnapshot
        };
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal RoundMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsUniqueConstraintFailure(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private sealed record CashSessionTotals(
        decimal CashSalesAmount,
        decimal CardSalesAmount,
        decimal TransferSalesAmount,
        decimal OtherSalesAmount,
        decimal CashInAmount,
        decimal CashOutAmount,
        decimal ExpectedCashAmount)
    {
        public static CashSessionTotals FromPersisted(CashSession session)
            => new(
                session.CashSalesAmount,
                session.CardSalesAmount,
                session.TransferSalesAmount,
                session.OtherSalesAmount,
                session.CashInAmount,
                session.CashOutAmount,
                session.ExpectedCashAmount);
    }
}
