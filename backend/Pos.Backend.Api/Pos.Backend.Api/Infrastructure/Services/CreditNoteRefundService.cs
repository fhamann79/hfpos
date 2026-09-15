using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CreditNoteRefundService : ICreditNoteRefundService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ICashSessionService _cashSessionService;
    private readonly ICreditNoteService _creditNoteService;
    private readonly IBusinessClockService _businessClock;
    private readonly ILogger<CreditNoteRefundService> _logger;

    public CreditNoteRefundService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ICashSessionService cashSessionService,
        ICreditNoteService creditNoteService,
        IBusinessClockService businessClock,
        ILogger<CreditNoteRefundService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _cashSessionService = cashSessionService;
        _creditNoteService = creditNoteService;
        _businessClock = businessClock;
        _logger = logger;
    }

    public async Task<CreditNoteDto> RefundAsync(int creditNoteId, RefundCreditNoteDto dto)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        if (dto?.Method is null || !Enum.IsDefined(dto.Method.Value))
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_METHOD_INVALID");
        }

        var reference = NormalizeOptionalText(dto.Reference);
        var notes = NormalizeOptionalText(dto.Notes);
        if (reference?.Length > 150)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_REFERENCE_TOO_LONG");
        }
        if (notes?.Length > 500)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_NOTES_TOO_LONG");
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        try
        {
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                var originalSaleId = await _context.CreditNotes.AsNoTracking()
                    .Where(note => note.Id == creditNoteId
                        && note.CompanyId == operationalContext.CompanyId
                        && note.EstablishmentId == operationalContext.EstablishmentId
                        && note.EmissionPointId == operationalContext.EmissionPointId)
                    .Select(note => (int?)note.OriginalSaleId)
                    .SingleOrDefaultAsync()
                    ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");

                // All note operations lock Sale -> CreditNote; cash is locked only after these.
                var sale = await _context.Sales.FromSqlInterpolated($@"
                    SELECT * FROM ""Sales""
                    WHERE ""Id"" = {originalSaleId}
                      AND ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                    FOR UPDATE")
                    .SingleOrDefaultAsync();
                if (sale is null)
                {
                    throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
                }

                var creditNote = await _context.CreditNotes.FromSqlInterpolated($@"
                    SELECT * FROM ""CreditNotes""
                    WHERE ""Id"" = {creditNoteId}
                      AND ""OriginalSaleId"" = {originalSaleId}
                      AND ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                    FOR UPDATE")
                    .SingleOrDefaultAsync()
                    ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");

                var existing = await _context.CreditNoteRefunds.AsNoTracking()
                    .Include(refund => refund.CashMovement)
                    .Include(refund => refund.CashSession)
                    .SingleOrDefaultAsync(refund => refund.CreditNoteId == creditNoteId);

                if (existing is not null)
                {
                    ValidateExistingRefund(creditNote, existing);
                }
                else
                {
                    ValidateCreditNoteState(creditNote);
                    var amount = RoundMoney(creditNote.Total);
                    if (amount <= 0m)
                    {
                        throw new InvalidOperationException("CREDIT_NOTE_REFUND_AMOUNT_INVALID");
                    }

                    CashMovement? cashMovement = null;
                    if (dto.Method.Value == SalePaymentMethod.Cash)
                    {
                        var number = NormalizeOptionalText(creditNote.Number) ?? $"#{creditNote.Id}";
                        cashMovement = await _cashSessionService.RegisterCreditNoteRefundCashOutAsync(
                            creditNote.Id, amount, $"Devoluci\u00f3n econ\u00f3mica nota de cr\u00e9dito {number}");
                    }

                    var now = _businessClock.UtcNow;
                    _context.CreditNoteRefunds.Add(new CreditNoteRefund
                    {
                        CreditNoteId = creditNote.Id,
                        CompanyId = operationalContext.CompanyId,
                        EstablishmentId = operationalContext.EstablishmentId,
                        EmissionPointId = operationalContext.EmissionPointId,
                        RefundedByUserId = operationalContext.UserId,
                        Method = dto.Method.Value,
                        Amount = amount,
                        RefundedAt = now,
                        BusinessDate = _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId),
                        TimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId,
                        CashSessionId = cashMovement?.CashSessionId,
                        CashMovementId = cashMovement?.Id,
                        Reference = reference,
                        Notes = notes
                    });
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation(
                    "Credit note refund completed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} UserId {UserId} AlreadyRefunded {AlreadyRefunded}",
                    creditNoteId, operationalContext.CompanyId, operationalContext.UserId, existing is not null);
            }

            return await _creditNoteService.GetByIdAsync(creditNoteId);
        }
        catch (Exception ex) when (IsDomainError(ex))
        {
            _logger.LogWarning(
                "Credit note refund rejected. CreditNoteId {CreditNoteId} CompanyId {CompanyId} UserId {UserId} ErrorCode {ErrorCode}",
                creditNoteId, operationalContext.CompanyId, operationalContext.UserId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Credit note refund failed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} UserId {UserId}",
                creditNoteId, operationalContext.CompanyId, operationalContext.UserId);
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_FAILED", ex);
        }
    }

    private static void ValidateCreditNoteState(CreditNote creditNote)
    {
        if (creditNote.DocumentStatus == SaleDocumentStatus.Cancelled || creditNote.VoidedAt.HasValue)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_CANCELLED");
        }
        if (creditNote.DocumentStatus == SaleDocumentStatus.Rejected)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_REJECTED");
        }

        var isAuthorized = creditNote.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(creditNote.SriAuthorizationStatus?.Trim(), "AUTORIZADO", StringComparison.OrdinalIgnoreCase);
        if (!isAuthorized
            || string.IsNullOrWhiteSpace(creditNote.AuthorizationNumber)
            || string.IsNullOrWhiteSpace(creditNote.AccessKey))
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_ONLY_AUTHORIZED");
        }
    }

    private static void ValidateExistingRefund(CreditNote creditNote, CreditNoteRefund refund)
    {
        var consistent = refund.Amount > 0m
            && refund.Amount == RoundMoney(creditNote.Total)
            && Enum.IsDefined(refund.Method)
            && refund.CompanyId == creditNote.CompanyId
            && refund.EstablishmentId == creditNote.EstablishmentId
            && refund.EmissionPointId == creditNote.EmissionPointId
            && refund.RefundedByUserId > 0
            && refund.RefundedAt != default
            && refund.BusinessDate != default
            && !string.IsNullOrWhiteSpace(refund.TimeZoneIdSnapshot);

        if (refund.Method == SalePaymentMethod.Cash)
        {
            var movement = refund.CashMovement;
            var session = refund.CashSession;
            consistent = consistent && movement is not null && session is not null
                && refund.CashSessionId == session.Id
                && refund.CashMovementId == movement.Id
                && movement.CashSessionId == session.Id
                && movement.Type == CashMovementType.CashOut
                && movement.Amount == refund.Amount
                && movement.CompanyId == refund.CompanyId
                && movement.EstablishmentId == refund.EstablishmentId
                && movement.EmissionPointId == refund.EmissionPointId
                && movement.UserId == refund.RefundedByUserId
                && session.CompanyId == refund.CompanyId
                && session.EstablishmentId == refund.EstablishmentId
                && session.EmissionPointId == refund.EmissionPointId
                && session.OpenedByUserId == refund.RefundedByUserId;
        }
        else
        {
            consistent = consistent && refund.CashSessionId is null && refund.CashMovementId is null;
        }

        if (!consistent)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REFUND_INCONSISTENT");
        }
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal RoundMoney(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsDomainError(Exception exception)
        => exception.Message is "CREDIT_NOTE_NOT_FOUND"
            or "CREDIT_NOTE_REFUND_CANCELLED"
            or "CREDIT_NOTE_REFUND_REJECTED"
            or "CREDIT_NOTE_REFUND_ONLY_AUTHORIZED"
            or "CREDIT_NOTE_REFUND_AMOUNT_INVALID"
            or "CREDIT_NOTE_REFUND_INCONSISTENT"
            or "CASH_SESSION_REQUIRED"
            or "CASH_SESSION_NOT_OPEN"
            or "CASH_SESSION_CONTEXT_MISMATCH"
            or "CASH_MOVEMENT_AMOUNT_INVALID"
            or "CASH_MOVEMENT_REASON_REQUIRED"
            or "CREDIT_NOTE_REFUND_FAILED";
}
