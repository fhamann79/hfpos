using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CreditNoteService : ICreditNoteService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ILogger<CreditNoteService> _logger;

    public CreditNoteService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ILogger<CreditNoteService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _logger = logger;
    }

    public async Task<CreditNoteEligibilityDto> GetEligibilityAsync(int originalSaleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            var originalSale = await _context.Sales
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(s =>
                    s.Id == originalSaleId
                    && s.CompanyId == operationalContext.CompanyId
                    && s.EstablishmentId == operationalContext.EstablishmentId
                    && s.EmissionPointId == operationalContext.EmissionPointId);

            if (originalSale is null)
            {
                throw new KeyNotFoundException("SALE_NOT_FOUND");
            }

            var creditedQuantities = await _context.CreditNoteItems
                .AsNoTracking()
                .Where(item =>
                    item.SaleItemId.HasValue
                    && item.CreditNote.OriginalSaleId == originalSaleId
                    && item.CreditNote.CompanyId == operationalContext.CompanyId
                    && item.CreditNote.VoidedAt == null
                    && (item.CreditNote.DocumentStatus == SaleDocumentStatus.Draft
                        || item.CreditNote.DocumentStatus == SaleDocumentStatus.PendingAuthorization
                        || item.CreditNote.DocumentStatus == SaleDocumentStatus.Authorized))
                .GroupBy(item => item.SaleItemId!.Value)
                .Select(group => new
                {
                    SaleItemId = group.Key,
                    Quantity = group.Sum(item => item.Quantity)
                })
                .ToDictionaryAsync(item => item.SaleItemId, item => item.Quantity);

            var items = originalSale.Items
                .OrderBy(item => item.Id)
                .Select(item =>
                {
                    var creditedQuantity = creditedQuantities.GetValueOrDefault(item.Id);

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
        catch (KeyNotFoundException)
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
}
