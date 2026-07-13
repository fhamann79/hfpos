using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CreditNoteService : ICreditNoteService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IFiscalDocumentNumberService _fiscalDocumentNumberService;
    private readonly IBusinessClockService _businessClockService;
    private readonly ILogger<CreditNoteService> _logger;

    public CreditNoteService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IFiscalDocumentNumberService fiscalDocumentNumberService,
        IBusinessClockService businessClockService,
        ILogger<CreditNoteService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _fiscalDocumentNumberService = fiscalDocumentNumberService;
        _businessClockService = businessClockService;
        _logger = logger;
    }

    public async Task<CreditNoteEligibilityDto> GetEligibilityAsync(int originalSaleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            var originalSale = await LoadOriginalSaleAsync(originalSaleId, operationalContext);
            var aggregates = await GetActiveCreditNoteItemAggregatesAsync(
                originalSaleId,
                operationalContext.CompanyId);

            return BuildEligibility(originalSale, aggregates);
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Credit note eligibility calculation failed. OriginalSaleId {OriginalSaleId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                originalSaleId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            throw new InvalidOperationException("CREDIT_NOTE_OPERATION_FAILED", ex);
        }
    }

    public async Task<CreditNoteDto> CreateDraftAsync(CreateCreditNoteDraftDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            var (reason, notes) = ValidateDraftRequest(dto);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var originalSale = await LockOriginalSaleAsync(dto.OriginalSaleId, operationalContext);
            var aggregates = await GetActiveCreditNoteItemAggregatesAsync(
                originalSale.Id,
                operationalContext.CompanyId);
            var eligibility = BuildEligibility(originalSale, aggregates);

            if (!eligibility.IsEligible)
            {
                throw new InvalidOperationException(
                    eligibility.BlockingCode ?? "CREDIT_NOTE_OPERATION_FAILED");
            }

            var originalItems = originalSale.Items.ToDictionary(item => item.Id);
            var availableItems = eligibility.Items.ToDictionary(item => item.SaleItemId);

            foreach (var requestedItem in dto.Items)
            {
                if (requestedItem.Quantity <= 0m)
                {
                    throw new InvalidOperationException("CREDIT_NOTE_INVALID_QUANTITY");
                }

                if (!originalItems.ContainsKey(requestedItem.SaleItemId)
                    || !availableItems.TryGetValue(requestedItem.SaleItemId, out var availableItem))
                {
                    throw new InvalidOperationException("CREDIT_NOTE_ITEM_NOT_FOUND");
                }

                if (requestedItem.Quantity > availableItem.AvailableQuantity)
                {
                    throw new InvalidOperationException("CREDIT_NOTE_QUANTITY_EXCEEDS_AVAILABLE");
                }
            }

            var now = _businessClockService.UtcNow;
            var creditNote = new CreditNote
            {
                CompanyId = operationalContext.CompanyId,
                EstablishmentId = operationalContext.EstablishmentId,
                EmissionPointId = operationalContext.EmissionPointId,
                UserId = operationalContext.UserId,
                OriginalSaleId = originalSale.Id,
                CustomerId = originalSale.CustomerId,
                BuyerNameSnapshot = FirstNonEmpty(
                    originalSale.BuyerNameSnapshot,
                    originalSale.Customer?.Name) ?? "CONSUMIDOR FINAL",
                BuyerIdentificationTypeSnapshot = FirstNonEmpty(
                    originalSale.BuyerIdentificationTypeSnapshot,
                    originalSale.Customer?.IdentificationType),
                BuyerIdentificationSnapshot = FirstNonEmpty(
                    originalSale.BuyerIdentificationSnapshot,
                    originalSale.Customer?.Identification),
                BuyerAddressSnapshot = FirstNonEmpty(
                    originalSale.BuyerAddressSnapshot,
                    originalSale.Customer?.Address),
                BuyerEmailSnapshot = FirstNonEmpty(
                    originalSale.BuyerEmailSnapshot,
                    originalSale.Customer?.Email),
                OriginalSaleNumberSnapshot = originalSale.Number,
                OriginalSaleAccessKeySnapshot = originalSale.AccessKey,
                OriginalSaleAuthorizationNumberSnapshot = originalSale.AuthorizationNumber,
                OriginalSaleAuthorizedAtSnapshot = originalSale.AuthorizedAt,
                OriginalSaleDocumentIssuedAtSnapshot = originalSale.DocumentIssuedAt,
                DocumentStatus = SaleDocumentStatus.Draft,
                Reason = reason,
                Notes = notes,
                BusinessDate = _businessClockService.GetBusinessDate(
                    now,
                    operationalContext.CompanyTimeZoneId),
                TimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId,
                CreatedAt = now,
                UpdatedAt = now,
                VoidedAt = null
            };

            foreach (var requestedItem in dto.Items.OrderBy(item => item.SaleItemId))
            {
                var originalItem = originalItems[requestedItem.SaleItemId];
                var availableItem = availableItems[requestedItem.SaleItemId];
                var aggregate = aggregates.GetValueOrDefault(requestedItem.SaleItemId)
                    ?? new CreditNoteItemAggregate { SaleItemId = requestedItem.SaleItemId };

                creditNote.Items.Add(CreateCreditNoteItem(
                    originalItem,
                    requestedItem.Quantity,
                    availableItem.AvailableQuantity,
                    aggregate));
            }

            ApplyCreditNoteTotals(creditNote);

            var numberAssignment = await _fiscalDocumentNumberService.AssignNextAsync(
                operationalContext,
                FiscalDocumentType.CreditNote);

            creditNote.Number = numberAssignment.Number;
            creditNote.EstablishmentCodeSnapshot = numberAssignment.EstablishmentCode;
            creditNote.EmissionPointCodeSnapshot = numberAssignment.EmissionPointCode;
            creditNote.Sequential = numberAssignment.Sequential;
            creditNote.DocumentIssuedAt = numberAssignment.IssuedAt;

            _context.CreditNotes.Add(creditNote);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return ToDto(creditNote, originalItems);
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Credit note draft creation failed. OriginalSaleId {OriginalSaleId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                dto.OriginalSaleId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            throw new InvalidOperationException("CREDIT_NOTE_OPERATION_FAILED", ex);
        }
    }

    public async Task<IReadOnlyList<CreditNoteListItemDto>> GetByOriginalSaleAsync(int originalSaleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            var originalSaleExists = await _context.Sales
                .AsNoTracking()
                .AnyAsync(sale =>
                    sale.Id == originalSaleId
                    && sale.CompanyId == operationalContext.CompanyId
                    && sale.EstablishmentId == operationalContext.EstablishmentId
                    && sale.EmissionPointId == operationalContext.EmissionPointId);

            if (!originalSaleExists)
            {
                throw new KeyNotFoundException("SALE_NOT_FOUND");
            }

            var creditNotes = await _context.CreditNotes
                .AsNoTracking()
                .Include(creditNote => creditNote.CancelledByUser)
                .Where(creditNote =>
                    creditNote.CompanyId == operationalContext.CompanyId
                    && creditNote.OriginalSaleId == originalSaleId)
                .OrderByDescending(creditNote => creditNote.CreatedAt)
                .ThenByDescending(creditNote => creditNote.Id)
                .ToListAsync();

            return creditNotes
                .Select(creditNote => ToListItemDto(creditNote, operationalContext))
                .ToList();
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Credit note history query failed. OriginalSaleId {OriginalSaleId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                originalSaleId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            throw new InvalidOperationException("CREDIT_NOTE_OPERATION_FAILED", ex);
        }
    }

    public async Task<CreditNoteDto> CancelDraftAsync(
        int creditNoteId,
        CancelCreditNoteDraftDto dto)
    {
        var cancellationReason = ValidateCancellationRequest(creditNoteId, dto);
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var originalSaleId = await GetCreditNoteOriginalSaleIdAsync(
                creditNoteId,
                operationalContext);

            await LockOriginalSaleRowAsync(originalSaleId, operationalContext);
            var creditNote = await LockCreditNoteAsync(creditNoteId, operationalContext);

            if (creditNote.DocumentStatus == SaleDocumentStatus.Cancelled
                || creditNote.VoidedAt.HasValue)
            {
                throw new InvalidOperationException("CREDIT_NOTE_DRAFT_ALREADY_CANCELLED");
            }

            if (creditNote.DocumentStatus != SaleDocumentStatus.Draft)
            {
                throw new InvalidOperationException("CREDIT_NOTE_DRAFT_NOT_CANCELLABLE");
            }

            if (HasSriProcessStarted(creditNote))
            {
                throw new InvalidOperationException("CREDIT_NOTE_DRAFT_SRI_PROCESS_STARTED");
            }

            var now = _businessClockService.UtcNow;
            creditNote.DocumentStatus = SaleDocumentStatus.Cancelled;
            creditNote.VoidedAt = now;
            creditNote.UpdatedAt = now;
            creditNote.CancellationReason = cancellationReason;
            creditNote.CancelledByUserId = operationalContext.UserId;
            creditNote.CancelledByUser = await _context.Users
                .SingleAsync(user => user.Id == operationalContext.UserId);

            await _context.Entry(creditNote)
                .Collection(note => note.Items)
                .Query()
                .Include(item => item.Product)
                .LoadAsync();

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return ToDto(creditNote);
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Credit note draft cancellation failed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                creditNoteId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            throw new InvalidOperationException("CREDIT_NOTE_OPERATION_FAILED", ex);
        }
    }

    private async Task<Sale> LoadOriginalSaleAsync(
        int originalSaleId,
        OperationalContext operationalContext)
    {
        var originalSale = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(s =>
                s.Id == originalSaleId
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        return originalSale ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    private async Task<Sale> LockOriginalSaleAsync(
        int originalSaleId,
        OperationalContext operationalContext)
    {
        var originalSale = await LockOriginalSaleRowAsync(originalSaleId, operationalContext);

        if (originalSale.CustomerId.HasValue)
        {
            await _context.Entry(originalSale)
                .Reference(s => s.Customer)
                .LoadAsync();
        }

        await _context.Entry(originalSale)
            .Collection(s => s.Items)
            .Query()
            .Include(item => item.Product)
            .LoadAsync();

        return originalSale;
    }

    private async Task<Sale> LockOriginalSaleRowAsync(
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
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        }

        return originalSale;
    }

    private async Task<int> GetCreditNoteOriginalSaleIdAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var originalSaleId = await _context.CreditNotes
            .AsNoTracking()
            .Where(creditNote =>
                creditNote.Id == creditNoteId
                && creditNote.CompanyId == operationalContext.CompanyId
                && creditNote.EstablishmentId == operationalContext.EstablishmentId
                && creditNote.EmissionPointId == operationalContext.EmissionPointId)
            .Select(creditNote => (int?)creditNote.OriginalSaleId)
            .SingleOrDefaultAsync();

        return originalSaleId
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private async Task<CreditNote> LockCreditNoteAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var creditNote = await _context.CreditNotes
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CreditNotes""
                WHERE ""Id"" = {creditNoteId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        return creditNote
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private async Task<IReadOnlyDictionary<int, CreditNoteItemAggregate>> GetActiveCreditNoteItemAggregatesAsync(
        int originalSaleId,
        int companyId)
    {
        var aggregates = await _context.CreditNoteItems
            .AsNoTracking()
            .Where(item =>
                item.SaleItemId.HasValue
                && item.CreditNote.OriginalSaleId == originalSaleId
                && item.CreditNote.CompanyId == companyId
                && item.CreditNote.VoidedAt == null
                && (item.CreditNote.DocumentStatus == SaleDocumentStatus.Draft
                    || item.CreditNote.DocumentStatus == SaleDocumentStatus.PendingAuthorization
                    || item.CreditNote.DocumentStatus == SaleDocumentStatus.Authorized))
            .GroupBy(item => item.SaleItemId!.Value)
            .Select(group => new CreditNoteItemAggregate
            {
                SaleItemId = group.Key,
                Quantity = group.Sum(item => item.Quantity),
                GrossSubtotal = group.Sum(item => item.GrossSubtotal),
                DiscountAmount = group.Sum(item => item.DiscountAmount),
                NetSubtotal = group.Sum(item => item.NetSubtotal),
                LineSubtotal = group.Sum(item => item.LineSubtotal),
                TaxableSubtotal = group.Sum(item => item.TaxableSubtotal),
                TaxAmount = group.Sum(item => item.TaxAmount),
                LineTotal = group.Sum(item => item.LineTotal),
                LineCost = group.Sum(item => item.LineCost)
            })
            .ToListAsync();

        return aggregates.ToDictionary(item => item.SaleItemId);
    }

    private static CreditNoteEligibilityDto BuildEligibility(
        Sale originalSale,
        IReadOnlyDictionary<int, CreditNoteItemAggregate> aggregates)
    {
        var items = originalSale.Items
            .OrderBy(item => item.Id)
            .Select(item =>
            {
                var creditedQuantity = aggregates.TryGetValue(item.Id, out var aggregate)
                    ? aggregate.Quantity
                    : 0m;

                return new CreditNoteEligibilityItemDto
                {
                    SaleItemId = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    SoldQuantity = item.Quantity,
                    CreditedQuantity = creditedQuantity,
                    AvailableQuantity = Math.Max(item.Quantity - creditedQuantity, 0m),
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    VatCategory = item.VatCategory,
                    VatRate = item.VatRate,
                    TaxableSubtotal = item.TaxableSubtotal,
                    TaxAmount = item.TaxAmount,
                    LineTotal = item.LineTotal
                };
            })
            .ToList();

        var eligibility = new CreditNoteEligibilityDto
        {
            OriginalSaleId = originalSale.Id,
            OriginalSaleNumber = originalSale.Number,
            OriginalSaleBusinessDate = originalSale.BusinessDate,
            OriginalSaleDocumentIssuedAt = originalSale.DocumentIssuedAt,
            OriginalSaleAccessKey = originalSale.AccessKey,
            OriginalSaleAuthorizationNumber = originalSale.AuthorizationNumber,
            OriginalSaleAuthorizedAt = originalSale.AuthorizedAt,
            BuyerName = FirstNonEmpty(originalSale.BuyerNameSnapshot, originalSale.Customer?.Name)
                ?? "CONSUMIDOR FINAL",
            BuyerIdentificationType = FirstNonEmpty(
                originalSale.BuyerIdentificationTypeSnapshot,
                originalSale.Customer?.IdentificationType),
            BuyerIdentification = FirstNonEmpty(
                originalSale.BuyerIdentificationSnapshot,
                originalSale.Customer?.Identification),
            BuyerAddress = FirstNonEmpty(
                originalSale.BuyerAddressSnapshot,
                originalSale.Customer?.Address),
            BuyerEmail = FirstNonEmpty(
                originalSale.BuyerEmailSnapshot,
                originalSale.Customer?.Email),
            OriginalTotal = originalSale.Total,
            Items = items
        };

        ApplyEligibilityRules(eligibility, originalSale);
        return eligibility;
    }

    private static CreditNoteItem CreateCreditNoteItem(
        SaleItem originalItem,
        decimal requestedQuantity,
        decimal availableQuantity,
        CreditNoteItemAggregate aggregate)
    {
        var useRemainingBalance = requestedQuantity == availableQuantity;
        var ratio = requestedQuantity / originalItem.Quantity;

        return new CreditNoteItem
        {
            SaleItemId = originalItem.Id,
            ProductId = originalItem.ProductId,
            Quantity = requestedQuantity,
            UnitPrice = originalItem.UnitPrice,
            UnitCost = originalItem.UnitCost,
            GrossSubtotal = Prorate(
                originalItem.GrossSubtotal,
                aggregate.GrossSubtotal,
                ratio,
                useRemainingBalance,
                2),
            DiscountAmount = Prorate(
                originalItem.DiscountAmount,
                aggregate.DiscountAmount,
                ratio,
                useRemainingBalance,
                2),
            NetSubtotal = Prorate(
                originalItem.NetSubtotal,
                aggregate.NetSubtotal,
                ratio,
                useRemainingBalance,
                2),
            LineSubtotal = Prorate(
                originalItem.LineSubtotal,
                aggregate.LineSubtotal,
                ratio,
                useRemainingBalance,
                2),
            VatCategory = originalItem.VatCategory,
            VatRate = originalItem.VatRate,
            TaxableSubtotal = Prorate(
                originalItem.TaxableSubtotal,
                aggregate.TaxableSubtotal,
                ratio,
                useRemainingBalance,
                2),
            TaxAmount = Prorate(
                originalItem.TaxAmount,
                aggregate.TaxAmount,
                ratio,
                useRemainingBalance,
                2),
            LineTotal = Prorate(
                originalItem.LineTotal,
                aggregate.LineTotal,
                ratio,
                useRemainingBalance,
                2),
            LineCost = Prorate(
                originalItem.LineCost,
                aggregate.LineCost,
                ratio,
                useRemainingBalance,
                4)
        };
    }

    private static void ApplyCreditNoteTotals(CreditNote creditNote)
    {
        creditNote.GrossSubtotal = Round(creditNote.Items.Sum(item => item.GrossSubtotal), 2);
        creditNote.DiscountAmount = Round(creditNote.Items.Sum(item => item.DiscountAmount), 2);
        creditNote.Subtotal = Round(creditNote.Items.Sum(item => item.TaxableSubtotal), 2);
        creditNote.TaxAmount = Round(creditNote.Items.Sum(item => item.TaxAmount), 2);
        creditNote.Vat15Subtotal = Round(creditNote.Items
            .Where(item => item.VatCategory == ProductVatCategory.Vat15)
            .Sum(item => item.TaxableSubtotal), 2);
        creditNote.Vat5Subtotal = Round(creditNote.Items
            .Where(item => item.VatCategory == ProductVatCategory.Vat5)
            .Sum(item => item.TaxableSubtotal), 2);
        creditNote.Vat0Subtotal = Round(creditNote.Items
            .Where(item => item.VatCategory == ProductVatCategory.Vat0)
            .Sum(item => item.TaxableSubtotal), 2);
        creditNote.VatExemptSubtotal = Round(creditNote.Items
            .Where(item => item.VatCategory == ProductVatCategory.VatExempt)
            .Sum(item => item.TaxableSubtotal), 2);
        creditNote.VatNotSubjectSubtotal = Round(creditNote.Items
            .Where(item => item.VatCategory == ProductVatCategory.VatNotSubject)
            .Sum(item => item.TaxableSubtotal), 2);
        creditNote.Total = Round(creditNote.Items.Sum(item => item.LineTotal), 2);
    }

    private static CreditNoteDto ToDto(
        CreditNote creditNote,
        IReadOnlyDictionary<int, SaleItem>? originalItems = null)
    {
        return new CreditNoteDto
        {
            Id = creditNote.Id,
            OriginalSaleId = creditNote.OriginalSaleId,
            OriginalSaleNumberSnapshot = creditNote.OriginalSaleNumberSnapshot,
            OriginalSaleAccessKeySnapshot = creditNote.OriginalSaleAccessKeySnapshot,
            OriginalSaleAuthorizationNumberSnapshot = creditNote.OriginalSaleAuthorizationNumberSnapshot,
            OriginalSaleAuthorizedAtSnapshot = creditNote.OriginalSaleAuthorizedAtSnapshot,
            OriginalSaleDocumentIssuedAtSnapshot = creditNote.OriginalSaleDocumentIssuedAtSnapshot,
            BuyerNameSnapshot = creditNote.BuyerNameSnapshot ?? "CONSUMIDOR FINAL",
            BuyerIdentificationTypeSnapshot = creditNote.BuyerIdentificationTypeSnapshot,
            BuyerIdentificationSnapshot = creditNote.BuyerIdentificationSnapshot,
            BuyerAddressSnapshot = creditNote.BuyerAddressSnapshot,
            BuyerEmailSnapshot = creditNote.BuyerEmailSnapshot,
            DocumentStatus = creditNote.DocumentStatus,
            Number = creditNote.Number,
            EstablishmentCodeSnapshot = creditNote.EstablishmentCodeSnapshot,
            EmissionPointCodeSnapshot = creditNote.EmissionPointCodeSnapshot,
            Sequential = creditNote.Sequential,
            DocumentIssuedAt = creditNote.DocumentIssuedAt,
            Reason = creditNote.Reason,
            Notes = creditNote.Notes,
            GrossSubtotal = creditNote.GrossSubtotal,
            DiscountAmount = creditNote.DiscountAmount,
            Subtotal = creditNote.Subtotal,
            TaxAmount = creditNote.TaxAmount,
            Vat15Subtotal = creditNote.Vat15Subtotal,
            Vat5Subtotal = creditNote.Vat5Subtotal,
            Vat0Subtotal = creditNote.Vat0Subtotal,
            VatExemptSubtotal = creditNote.VatExemptSubtotal,
            VatNotSubjectSubtotal = creditNote.VatNotSubjectSubtotal,
            Total = creditNote.Total,
            BusinessDate = creditNote.BusinessDate,
            TimeZoneIdSnapshot = creditNote.TimeZoneIdSnapshot,
            CreatedAt = creditNote.CreatedAt,
            UpdatedAt = creditNote.UpdatedAt,
            VoidedAt = creditNote.VoidedAt,
            CancellationReason = creditNote.CancellationReason,
            CancelledByUserId = creditNote.CancelledByUserId,
            CancelledByUsername = creditNote.CancelledByUser?.Username,
            Items = creditNote.Items
                .OrderBy(item => item.SaleItemId)
                .Select(item => new CreditNoteItemDto
                {
                    Id = item.Id,
                    SaleItemId = item.SaleItemId,
                    ProductId = item.ProductId,
                    ProductName = ResolveProductName(item, originalItems),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    GrossSubtotal = item.GrossSubtotal,
                    DiscountAmount = item.DiscountAmount,
                    NetSubtotal = item.NetSubtotal,
                    LineSubtotal = item.LineSubtotal,
                    VatCategory = item.VatCategory,
                    VatRate = item.VatRate,
                    TaxableSubtotal = item.TaxableSubtotal,
                    TaxAmount = item.TaxAmount,
                    LineTotal = item.LineTotal
                })
                .ToList()
        };
    }

    private static CreditNoteListItemDto ToListItemDto(
        CreditNote creditNote,
        OperationalContext operationalContext)
    {
        return new CreditNoteListItemDto
        {
            Id = creditNote.Id,
            OriginalSaleId = creditNote.OriginalSaleId,
            Number = creditNote.Number,
            EstablishmentCodeSnapshot = creditNote.EstablishmentCodeSnapshot,
            EmissionPointCodeSnapshot = creditNote.EmissionPointCodeSnapshot,
            DocumentStatus = creditNote.DocumentStatus,
            DocumentIssuedAt = creditNote.DocumentIssuedAt,
            BusinessDate = creditNote.BusinessDate,
            CreatedAt = creditNote.CreatedAt,
            Reason = creditNote.Reason,
            Total = creditNote.Total,
            VoidedAt = creditNote.VoidedAt,
            CancellationReason = creditNote.CancellationReason,
            CancelledByUserId = creditNote.CancelledByUserId,
            CancelledByUsername = creditNote.CancelledByUser?.Username,
            CanCancelDraft = creditNote.CompanyId == operationalContext.CompanyId
                && creditNote.EstablishmentId == operationalContext.EstablishmentId
                && creditNote.EmissionPointId == operationalContext.EmissionPointId
                && creditNote.DocumentStatus == SaleDocumentStatus.Draft
                && !creditNote.VoidedAt.HasValue
                && !HasSriProcessStarted(creditNote)
        };
    }

    private static string ResolveProductName(
        CreditNoteItem item,
        IReadOnlyDictionary<int, SaleItem>? originalItems)
    {
        if (item.Product is not null && !string.IsNullOrWhiteSpace(item.Product.Name))
        {
            return item.Product.Name;
        }

        if (item.SaleItemId.HasValue
            && originalItems is not null
            && originalItems.TryGetValue(item.SaleItemId.Value, out var originalItem))
        {
            return originalItem.Product.Name;
        }

        return "Producto";
    }

    private static (string Reason, string? Notes) ValidateDraftRequest(CreateCreditNoteDraftDto dto)
    {
        if (dto.OriginalSaleId <= 0)
        {
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        }

        var reason = dto.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REASON_REQUIRED");
        }

        if (reason.Length > 300)
        {
            throw new InvalidOperationException("CREDIT_NOTE_REASON_TOO_LONG");
        }

        var notes = NormalizeOptional(dto.Notes);
        if (notes?.Length > 500)
        {
            throw new InvalidOperationException("CREDIT_NOTE_NOTES_TOO_LONG");
        }

        if (dto.Items is null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("CREDIT_NOTE_ITEMS_REQUIRED");
        }

        if (dto.Items.GroupBy(item => item.SaleItemId).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("CREDIT_NOTE_DUPLICATE_SALE_ITEM");
        }

        if (dto.Items.Any(item => item.Quantity <= 0m))
        {
            throw new InvalidOperationException("CREDIT_NOTE_INVALID_QUANTITY");
        }

        return (reason, notes);
    }

    private static string ValidateCancellationRequest(
        int creditNoteId,
        CancelCreditNoteDraftDto dto)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var reason = dto?.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new InvalidOperationException("CREDIT_NOTE_CANCELLATION_REASON_REQUIRED");
        }

        if (reason.Length > 300)
        {
            throw new InvalidOperationException("CREDIT_NOTE_CANCELLATION_REASON_TOO_LONG");
        }

        return reason;
    }

    private static bool HasSriProcessStarted(CreditNote creditNote)
    {
        return !string.IsNullOrWhiteSpace(creditNote.AccessKey)
            || creditNote.SriSubmittedAt.HasValue
            || !string.IsNullOrWhiteSpace(creditNote.SriReceptionStatus)
            || !string.IsNullOrWhiteSpace(creditNote.SriAuthorizationStatus)
            || !string.IsNullOrWhiteSpace(creditNote.AuthorizationNumber)
            || creditNote.AuthorizedAt.HasValue
            || creditNote.SriLastCheckedAt.HasValue;
    }

    private static void ApplyEligibilityRules(CreditNoteEligibilityDto eligibility, Sale originalSale)
    {
        if (originalSale.DocumentType != SaleDocumentType.Invoice)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_NOT_INVOICE",
                "Solo una factura puede recibir una nota de crédito.");
            return;
        }

        if (originalSale.Status == SaleStatus.Voided || originalSale.VoidedAt.HasValue)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_VOIDED",
                "La venta original está anulada.");
            return;
        }

        if (originalSale.Status != SaleStatus.Completed)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_NOT_COMPLETED",
                "La venta original no está completada.");
            return;
        }

        if (originalSale.DocumentStatus != SaleDocumentStatus.Authorized)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_NOT_AUTHORIZED",
                "La factura original todavía no está autorizada por el SRI.");
            return;
        }

        if (string.IsNullOrWhiteSpace(originalSale.AccessKey)
            || string.IsNullOrWhiteSpace(originalSale.AuthorizationNumber)
            || !originalSale.AuthorizedAt.HasValue)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_AUTHORIZATION_DATA_REQUIRED",
                "La factura no contiene todos los datos de autorización requeridos.");
            return;
        }

        if (eligibility.Items.Count == 0)
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_WITHOUT_ITEMS",
                "La factura original no contiene ítems.");
            return;
        }

        if (!eligibility.Items.Any(item => item.AvailableQuantity > 0m))
        {
            Block(
                eligibility,
                "CREDIT_NOTE_ORIGINAL_SALE_FULLY_CREDITED",
                "La factura ya fue acreditada completamente.");
            return;
        }

        eligibility.IsEligible = true;
        eligibility.BlockingCode = null;
        eligibility.BlockingMessage = null;
    }

    private static void Block(CreditNoteEligibilityDto eligibility, string code, string message)
    {
        eligibility.IsEligible = false;
        eligibility.BlockingCode = code;
        eligibility.BlockingMessage = message;
    }

    private static decimal Prorate(
        decimal originalValue,
        decimal previouslyCreditedValue,
        decimal ratio,
        bool useRemainingBalance,
        int decimals)
    {
        var value = useRemainingBalance
            ? originalValue - previouslyCreditedValue
            : originalValue * ratio;

        return Round(value, decimals);
    }

    private static decimal Round(decimal value, int decimals)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = value?.Trim();
            if (!string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private sealed class CreditNoteItemAggregate
    {
        public int SaleItemId { get; set; }
        public decimal Quantity { get; set; }
        public decimal GrossSubtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal NetSubtotal { get; set; }
        public decimal LineSubtotal { get; set; }
        public decimal TaxableSubtotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public decimal LineCost { get; set; }
    }
}
