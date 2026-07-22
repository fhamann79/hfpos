using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CreditNoteInventoryReturnService : ICreditNoteInventoryReturnService
{
    private const int MaxNotesLength = 500;
    private const int MaxMovementNotesLength = 1000;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IInventoryService _inventoryService;
    private readonly ICreditNoteService _creditNoteService;
    private readonly IBusinessClockService _businessClock;
    private readonly ILogger<CreditNoteInventoryReturnService> _logger;

    public CreditNoteInventoryReturnService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IInventoryService inventoryService,
        ICreditNoteService creditNoteService,
        IBusinessClockService businessClock,
        ILogger<CreditNoteInventoryReturnService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _inventoryService = inventoryService;
        _creditNoteService = creditNoteService;
        _businessClock = businessClock;
        _logger = logger;
    }

    public async Task<CreditNoteDto> ReturnToInventoryAsync(
        int creditNoteId,
        ReturnCreditNoteInventoryDto dto)
    {
        if (creditNoteId <= 0)
        {
            _logger.LogWarning(
                "Credit note inventory return rejected. CreditNoteId {CreditNoteId} ErrorCode {ErrorCode}",
                creditNoteId,
                "CREDIT_NOTE_NOT_FOUND");
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var notes = NormalizeOptionalText(dto?.Notes);
        if (notes?.Length > MaxNotesLength)
        {
            _logger.LogWarning(
                "Credit note inventory return rejected. CreditNoteId {CreditNoteId} ErrorCode {ErrorCode}",
                creditNoteId,
                "CREDIT_NOTE_INVENTORY_RETURN_NOTES_TOO_LONG");
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_NOTES_TOO_LONG");
        }

        var operationalContext = await _operationalContextAccessor
            .GetRequiredContextAsync();

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync();

            var originalSaleId = await GetOriginalSaleIdAsync(
                creditNoteId,
                operationalContext);

            await LockOriginalSaleAsync(originalSaleId, operationalContext);

            var creditNote = await LockCreditNoteAsync(
                creditNoteId,
                originalSaleId,
                operationalContext);

            var items = await _context.CreditNoteItems
                .AsNoTracking()
                .Where(item => item.CreditNoteId == creditNote.Id)
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.Id)
                .ToListAsync();

            var existingMovements = await _context.InventoryMovements
                .AsNoTracking()
                .Where(movement =>
                    movement.CompanyId == creditNote.CompanyId
                    && movement.EstablishmentId == creditNote.EstablishmentId
                    && movement.SourceType
                        == InventoryMovementSourceType.CreditNoteReturn
                    && movement.SourceId == creditNote.Id)
                .ToListAsync();

            var hasCompleteAudit = creditNote.InventoryReturnedAt.HasValue
                && creditNote.InventoryReturnedByUserId.HasValue;
            var hasCompleteMovements = HasCompleteMovementSet(
                creditNote,
                items,
                existingMovements);

            if (hasCompleteAudit && hasCompleteMovements)
            {
                await transaction.CommitAsync();
                LogSuccess(
                    creditNote,
                    operationalContext,
                    items.Count,
                    alreadyReturned: true);
                return await _creditNoteService.GetByIdAsync(creditNoteId);
            }

            var hasAuditData = creditNote.InventoryReturnedAt.HasValue
                || creditNote.InventoryReturnedByUserId.HasValue
                || creditNote.InventoryReturnNotes is not null;

            if (hasAuditData || existingMovements.Count > 0)
            {
                throw new InvalidOperationException(
                    "CREDIT_NOTE_INVENTORY_RETURN_INCONSISTENT");
            }

            ValidateCreditNoteState(creditNote);
            ValidateItems(items);

            var movementNotes = BuildMovementNotes(creditNote, notes);
            foreach (var item in items)
            {
                await _inventoryService.RegisterCreditNoteReturnAsync(
                    item.ProductId,
                    item.Quantity,
                    creditNote.Id,
                    item.Id,
                    movementNotes);
            }

            var now = _businessClock.UtcNow;
            creditNote.InventoryReturnedAt = now;
            creditNote.InventoryReturnedByUserId = operationalContext.UserId;
            creditNote.InventoryReturnNotes = notes;
            creditNote.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            LogSuccess(
                creditNote,
                operationalContext,
                items.Count,
                alreadyReturned: false);

            return await _creditNoteService.GetByIdAsync(creditNoteId);
        }
        catch (Exception ex) when (IsDomainError(ex))
        {
            _logger.LogWarning(
                "Credit note inventory return rejected. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} UserId {UserId} ErrorCode {ErrorCode}",
                creditNoteId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                operationalContext.UserId,
                ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Credit note inventory return failed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} UserId {UserId}",
                creditNoteId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                operationalContext.UserId);
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_FAILED",
                ex);
        }
    }

    private async Task<int> GetOriginalSaleIdAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var originalSaleId = await _context.CreditNotes
            .AsNoTracking()
            .Where(note =>
                note.Id == creditNoteId
                && note.CompanyId == operationalContext.CompanyId
                && note.EstablishmentId == operationalContext.EstablishmentId
                && note.EmissionPointId == operationalContext.EmissionPointId)
            .Select(note => (int?)note.OriginalSaleId)
            .SingleOrDefaultAsync();

        return originalSaleId
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private async Task LockOriginalSaleAsync(
        int originalSaleId,
        OperationalContext operationalContext)
    {
        var originalSale = await _context.Sales
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Sales""
                WHERE ""Id"" = {originalSaleId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        if (originalSale is null)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }
    }

    private async Task<CreditNote> LockCreditNoteAsync(
        int creditNoteId,
        int originalSaleId,
        OperationalContext operationalContext)
    {
        var creditNote = await _context.CreditNotes
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CreditNotes""
                WHERE ""Id"" = {creditNoteId}
                  AND ""OriginalSaleId"" = {originalSaleId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        return creditNote
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private static bool HasCompleteMovementSet(
        CreditNote creditNote,
        IReadOnlyCollection<CreditNoteItem> items,
        IReadOnlyCollection<InventoryMovement> movements)
    {
        if (items.Count == 0 || movements.Count != items.Count)
        {
            return false;
        }

        foreach (var item in items)
        {
            var matches = movements
                .Where(movement => movement.SourceLineId == item.Id)
                .ToList();

            if (matches.Count != 1)
            {
                return false;
            }

            var movement = matches[0];
            if (movement.SourceId != creditNote.Id
                || movement.ProductId != item.ProductId
                || movement.Quantity != item.Quantity
                || movement.Type != InventoryMovementType.Return
                || movement.SourceType
                    != InventoryMovementSourceType.CreditNoteReturn
                || movement.CompanyId != creditNote.CompanyId
                || movement.EstablishmentId != creditNote.EstablishmentId)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateCreditNoteState(CreditNote creditNote)
    {
        if (creditNote.DocumentStatus == SaleDocumentStatus.Cancelled
            || creditNote.VoidedAt.HasValue)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_CANCELLED");
        }

        if (creditNote.DocumentStatus == SaleDocumentStatus.Rejected)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_REJECTED");
        }

        var isAuthorized =
            creditNote.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(
                creditNote.SriAuthorizationStatus?.Trim(),
                "AUTORIZADO",
                StringComparison.OrdinalIgnoreCase);

        if (!isAuthorized
            || string.IsNullOrWhiteSpace(creditNote.AuthorizationNumber)
            || string.IsNullOrWhiteSpace(creditNote.AccessKey))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_ONLY_AUTHORIZED");
        }
    }

    private static void ValidateItems(IReadOnlyCollection<CreditNoteItem> items)
    {
        if (items.Count == 0
            || items.Any(item =>
                item.Id <= 0
                || item.ProductId <= 0
                || item.Quantity <= 0m
                || !item.SaleItemId.HasValue))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_INVENTORY_RETURN_ITEM_INVALID");
        }
    }

    private static string BuildMovementNotes(
        CreditNote creditNote,
        string? notes)
    {
        var number = string.IsNullOrWhiteSpace(creditNote.Number)
            ? $"#{creditNote.Id}"
            : creditNote.Number.Trim();
        var movementNotes =
            $"Devolución por nota de crédito {number}. Motivo fiscal: {creditNote.Reason.Trim()}.";

        if (notes is not null)
        {
            movementNotes = $"{movementNotes} Observaciones: {notes}";
        }

        return movementNotes.Length <= MaxMovementNotesLength
            ? movementNotes
            : movementNotes[..MaxMovementNotesLength].TrimEnd();
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsDomainError(Exception exception)
    {
        return exception.Message is
            "CREDIT_NOTE_NOT_FOUND"
            or "PRODUCT_NOT_FOUND"
            or "CREDIT_NOTE_ITEM_NOT_FOUND"
            or "CREDIT_NOTE_INVENTORY_RETURN_NOTES_TOO_LONG"
            or "CREDIT_NOTE_INVENTORY_RETURN_ITEM_INVALID"
            or "CREDIT_NOTE_INVENTORY_RETURN_CANCELLED"
            or "CREDIT_NOTE_INVENTORY_RETURN_REJECTED"
            or "CREDIT_NOTE_INVENTORY_RETURN_ONLY_AUTHORIZED"
            or "CREDIT_NOTE_INVENTORY_RETURN_INCONSISTENT"
            or "INVENTORY_CONCURRENCY_CONFLICT"
            or "INVALID_QUANTITY";
    }

    private void LogSuccess(
        CreditNote creditNote,
        OperationalContext operationalContext,
        int itemsCount,
        bool alreadyReturned)
    {
        _logger.LogInformation(
            "Credit note inventory return completed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount} UserId {UserId} AlreadyReturned {AlreadyReturned}",
            creditNote.Id,
            operationalContext.CompanyId,
            operationalContext.EstablishmentId,
            operationalContext.EmissionPointId,
            itemsCount,
            operationalContext.UserId,
            alreadyReturned);
    }
}
