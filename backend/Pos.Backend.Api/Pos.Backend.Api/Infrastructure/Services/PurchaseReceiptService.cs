using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PurchaseReceiptService(
    PosDbContext context, IInventoryService inventoryService,
    IOperationalContextAccessor operationalContextAccessor, IBusinessClockService businessClock,
    TenantAdministrationGuard administrationGuard, IProductCostService productCostService) : IPurchaseReceiptService
{
    private readonly PosDbContext _context = context;
    private readonly IInventoryService _inventoryService = inventoryService;
    private readonly IOperationalContextAccessor _operationalContextAccessor = operationalContextAccessor;
    private readonly IBusinessClockService _businessClock = businessClock;
    private readonly TenantAdministrationGuard _administrationGuard = administrationGuard;
    private readonly IProductCostService _productCostService = productCostService;

    public async Task<PurchaseReceiptDto> CreateAsync(PurchaseReceiptCreateDto dto)
    {
        if (dto is null || dto.SupplierId <= 0)
        {
            throw new InvalidOperationException("PURCHASE_RECEIPT_SUPPLIER_REQUIRED");
        }
        CriticalOperationRequest.RequireId(dto.RequestId);

        if (dto.Items is null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("PURCHASE_RECEIPT_ITEMS_REQUIRED");
        }

        foreach (var item in dto.Items)
        {
            if (item is null || RoundQuantity(item.Quantity) <= 0m)
            {
                throw new InvalidOperationException("PURCHASE_RECEIPT_QUANTITY_INVALID");
            }

            if (item.UnitCost < 0m)
            {
                throw new InvalidOperationException("PURCHASE_RECEIPT_UNIT_COST_INVALID");
            }
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await _administrationGuard.LockExclusiveOperationalWriteAsync(operationalContext);
            var hash = CriticalOperationRequest.Hash(new
            {
                Version = 1, dto.SupplierId,
                ReceiptNumber = NormalizeOptionalText(dto.ReceiptNumber),
                SupplierDocumentNumber = NormalizeOptionalText(dto.SupplierDocumentNumber),
                ReceiptDate = dto.ReceiptDate == default ? (DateOnly?)null : DateOnly.FromDateTime(dto.ReceiptDate),
                Notes = NormalizeOptionalText(dto.Notes),
                Items = dto.Items.Select(i => new { i.ProductId, Quantity = RoundQuantity(i.Quantity),
                    UnitCost = RoundMoney(i.UnitCost), Notes = NormalizeOptionalText(i.Notes) }).ToArray()
            });
            var existing = await _context.PurchaseReceipts.AsNoTracking().SingleOrDefaultAsync(r =>
                r.CompanyId == operationalContext.CompanyId && r.RequestId == dto.RequestId);
            if (existing is not null)
            {
                if (existing.RequestHash != hash || existing.CreatedByUserId != operationalContext.UserId
                    || existing.EstablishmentId != operationalContext.EstablishmentId
                    || existing.RequestEmissionPointId != operationalContext.EmissionPointId)
                    throw new InvalidOperationException("REQUEST_CONFLICT");
                var replay = await LoadReceiptDtoAsync(existing.Id, operationalContext.CompanyId, operationalContext.EstablishmentId);
                await transaction.CommitAsync();
                return replay;
            }

            var supplierExists = await _context.Suppliers.AnyAsync(s =>
                s.Id == dto.SupplierId
                && s.CompanyId == operationalContext.CompanyId
                && s.IsActive);

            if (!supplierExists)
            {
                throw new KeyNotFoundException("SUPPLIER_NOT_FOUND");
            }

            var productIds = dto.Items
                .Select(item => item.ProductId)
                .Distinct()
                .ToArray();
            var productById = await _productCostService.LockProductsAsync(
                operationalContext.CompanyId,
                productIds);

            if (productById.Count != productIds.Length)
            {
                throw new KeyNotFoundException("PRODUCT_NOT_FOUND");
            }

            if (productById.Values.Any(product => !product.IsActive))
            {
                throw new InvalidOperationException("PRODUCT_INACTIVE");
            }

            var now = _businessClock.UtcNow;
            var businessDate = dto.ReceiptDate == default
                ? _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId)
                : DateOnly.FromDateTime(dto.ReceiptDate);
            var receiptDate = _businessClock.GetBusinessDateStartUtc(
                businessDate,
                operationalContext.CompanyTimeZoneId);
            var receiptItems = new List<PurchaseReceiptItem>();

            foreach (var itemDto in dto.Items)
            {
                var product = productById[itemDto.ProductId];
                var unitCost = RoundMoney(itemDto.UnitCost);
                var quantity = RoundQuantity(itemDto.Quantity);
                var lineTotal = RoundMoney(quantity * unitCost);
                var receiptItem = new PurchaseReceiptItem
                {
                    ProductId = product.Id,
                    Quantity = quantity,
                    UnitCost = unitCost,
                    LineTotal = lineTotal,
                    Notes = NormalizeOptionalText(itemDto.Notes)
                };

                receiptItems.Add(receiptItem);
                _productCostService.ApplyPurchaseReceiptCost(
                    product,
                    receiptItem,
                    operationalContext.UserId,
                    now);
            }

            var receipt = new PurchaseReceipt
            {
                RequestId = dto.RequestId,
                RequestHash = hash,
                RequestEmissionPointId = operationalContext.EmissionPointId,
                CompanyId = operationalContext.CompanyId,
                EstablishmentId = operationalContext.EstablishmentId,
                SupplierId = dto.SupplierId,
                ReceiptNumber = NormalizeOptionalText(dto.ReceiptNumber),
                SupplierDocumentNumber = NormalizeOptionalText(dto.SupplierDocumentNumber),
                ReceiptDate = receiptDate,
                ReceiptBusinessDate = businessDate,
                ReceiptTimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId,
                Status = PurchaseReceiptStatus.Posted,
                Subtotal = RoundMoney(receiptItems.Sum(i => i.LineTotal)),
                Notes = NormalizeOptionalText(dto.Notes),
                CreatedAt = now,
                CreatedByUserId = operationalContext.UserId,
                PostedAt = now,
                Items = receiptItems
            };

            _context.PurchaseReceipts.Add(receipt);
            await _context.SaveChangesAsync();

            foreach (var item in receipt.Items.OrderBy(item => item.ProductId).ThenBy(item => item.Id))
            {
                await _inventoryService.RegisterPurchaseReceiptAsync(
                    item.ProductId,
                    item.Quantity,
                    receipt.Id,
                    item.Id,
                    item.Notes ?? receipt.SupplierDocumentNumber);
            }

            var response = await LoadReceiptDtoAsync(receipt.Id, operationalContext.CompanyId, operationalContext.EstablishmentId);
            await transaction.CommitAsync();
            return response;
        }
        catch
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }
    }
    private async Task<PurchaseReceiptDto> LoadReceiptDtoAsync(int id, int companyId, int establishmentId)
    {
        return await _context.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.Id == id && r.CompanyId == companyId && r.EstablishmentId == establishmentId)
            .Select(r => new PurchaseReceiptDto
            {
                Id = r.Id,
                RequestId = r.RequestId,
                SupplierId = r.SupplierId,
                SupplierName = r.Supplier.Name,
                ReceiptNumber = r.ReceiptNumber,
                SupplierDocumentNumber = r.SupplierDocumentNumber,
                ReceiptDate = r.ReceiptDate,
                ReceiptBusinessDate = r.ReceiptBusinessDate,
                ReceiptTimeZoneIdSnapshot = r.ReceiptTimeZoneIdSnapshot,
                Status = r.Status,
                Subtotal = r.Subtotal,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUsername = r.CreatedByUser.Username,
                PostedAt = r.PostedAt,
                CanceledAt = r.CanceledAt,
                CanceledBusinessDate = r.CanceledBusinessDate,
                CanceledTimeZoneIdSnapshot = r.CanceledTimeZoneIdSnapshot,
                CanceledByUserId = r.CanceledByUserId,
                CanceledByUsername = r.CanceledByUser != null ? r.CanceledByUser.Username : null,
                CancelReason = r.CancelReason,
                Items = r.Items
                    .OrderBy(i => i.Id)
                    .Select(i => new PurchaseReceiptItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product.Name,
                        Quantity = i.Quantity,
                        UnitCost = i.UnitCost,
                        LineTotal = i.LineTotal,
                        PreviousProductCost = i.PreviousProductCost,
                        AppliedProductCost = i.AppliedProductCost,
                        ProductCostChangedOnCancellation = i.ProductCostChangedOnCancellation,
                        ProductCostAfterCancellation = i.ProductCostAfterCancellation,
                        Notes = i.Notes
                    })
                    .ToList()
            })
            .FirstAsync();
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundQuantity(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

}
