using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class PurchaseReceiptsController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IBusinessClockService _businessClock;
    private readonly TenantAdministrationGuard _administrationGuard;
    private readonly IProductCostService _productCostService;
    private readonly IPurchaseReceiptQueryService _purchaseReceiptQueryService;

    public PurchaseReceiptsController(
        PosDbContext context,
        IInventoryService inventoryService,
        IOperationalContextAccessor operationalContextAccessor,
        IBusinessClockService businessClock,
        TenantAdministrationGuard administrationGuard,
        IProductCostService productCostService,
        IPurchaseReceiptQueryService purchaseReceiptQueryService)
    {
        _context = context;
        _inventoryService = inventoryService;
        _operationalContextAccessor = operationalContextAccessor;
        _businessClock = businessClock;
        _administrationGuard = administrationGuard;
        _productCostService = productCostService;
        _purchaseReceiptQueryService = purchaseReceiptQueryService;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.PurchasesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurchaseReceiptListResultDto>> Get(
        [FromQuery] PurchaseReceiptListQueryDto query)
    {
        return Ok(await _purchaseReceiptQueryService.GetListAsync(query));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.PurchasesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurchaseReceiptDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var receipt = await _context.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.Id == id
                && r.CompanyId == operationalContext.CompanyId
                && r.EstablishmentId == operationalContext.EstablishmentId)
            .Select(r => new PurchaseReceiptDto
            {
                Id = r.Id,
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
            .FirstOrDefaultAsync();

        if (receipt is null)
        {
            return NotFound(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_NOT_FOUND" });
        }

        return Ok(receipt);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.PurchasesWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurchaseReceiptDto>> Create([FromBody] PurchaseReceiptCreateDto dto)
    {
        if (dto is null || dto.SupplierId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_SUPPLIER_REQUIRED" });
        }

        if (dto.Items is null || dto.Items.Count == 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_ITEMS_REQUIRED" });
        }

        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0m)
            {
                return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_QUANTITY_INVALID" });
            }

            if (item.UnitCost < 0m)
            {
                return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_UNIT_COST_INVALID" });
            }
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await _administrationGuard.LockOperationalWriteAsync(operationalContext);

            var supplierExists = await _context.Suppliers.AnyAsync(s =>
                s.Id == dto.SupplierId
                && s.CompanyId == operationalContext.CompanyId
                && s.IsActive);

            if (!supplierExists)
            {
                return NotFound(new ApiErrorResponse { Error = "SUPPLIER_NOT_FOUND" });
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
                return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
            }

            if (productById.Values.Any(product => !product.IsActive))
            {
                return BadRequest(new ApiErrorResponse { Error = "PRODUCT_INACTIVE" });
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

            await transaction.CommitAsync();

            var response = await LoadReceiptDtoAsync(receipt.Id, operationalContext.CompanyId, operationalContext.EstablishmentId);
            return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, response);
        }
        catch (InvalidOperationException ex) when (TryMapInventoryError(ex.Message, out var result))
        {
            await transaction.RollbackAsync();
            return result;
        }
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = AppPermissions.PurchasesWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PurchaseReceiptDto>> Cancel(int id, [FromBody] CancelPurchaseReceiptDto dto)
    {
        var reason = NormalizeOptionalText(dto?.Reason);

        if (reason is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_CANCEL_REASON_REQUIRED" });
        }

        if (reason.Length > 500)
        {
            return BadRequest(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_CANCEL_REASON_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await _administrationGuard.LockOperationalWriteAsync(operationalContext);
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1
                FROM "PurchaseReceipts"
                WHERE "Id" = {id}
                  AND "CompanyId" = {operationalContext.CompanyId}
                  AND "EstablishmentId" = {operationalContext.EstablishmentId}
                FOR UPDATE
                """);

            var receipt = await _context.PurchaseReceipts
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id
                    && r.CompanyId == operationalContext.CompanyId
                    && r.EstablishmentId == operationalContext.EstablishmentId);

            if (receipt is null)
            {
                return NotFound(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_NOT_FOUND" });
            }

            if (receipt.Status == PurchaseReceiptStatus.Canceled)
            {
                return Conflict(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_ALREADY_CANCELED" });
            }

            var productIds = receipt.Items
                .Select(item => item.ProductId)
                .Distinct()
                .ToArray();
            var productById = await _productCostService.LockProductsAsync(
                operationalContext.CompanyId,
                productIds);

            if (productById.Count != productIds.Length)
            {
                return Conflict(new ApiErrorResponse { Error = "PRODUCT_COST_PROVENANCE_INVALID" });
            }

            foreach (var productItems in receipt.Items
                .GroupBy(item => item.ProductId)
                .OrderBy(group => group.Key))
            {
                await _productCostService.ResolveCancellationAsync(
                    operationalContext.CompanyId,
                    receipt,
                    productById[productItems.Key],
                    productItems.ToArray());
            }

            foreach (var item in receipt.Items.OrderBy(item => item.ProductId).ThenBy(item => item.Id))
            {
                await _inventoryService.RegisterPurchaseReceiptCancelAsync(
                    item.ProductId,
                    item.Quantity,
                    receipt.Id,
                    item.Id,
                    reason);
            }

            var now = _businessClock.UtcNow;
            receipt.Status = PurchaseReceiptStatus.Canceled;
            receipt.CanceledAt = now;
            receipt.CanceledBusinessDate = _businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId);
            receipt.CanceledTimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId;
            receipt.CanceledByUserId = operationalContext.UserId;
            receipt.CancelReason = reason;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = await LoadReceiptDtoAsync(receipt.Id, operationalContext.CompanyId, operationalContext.EstablishmentId);
            return Ok(response);
        }
        catch (InvalidOperationException ex) when (TryMapPurchaseReceiptCancelError(ex.Message, out var result))
        {
            await transaction.RollbackAsync();
            return result;
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

    private static bool TryMapInventoryError(string error, out ActionResult result)
    {
        result = error switch
        {
            "PRODUCT_NOT_FOUND" => new NotFoundObjectResult(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" }),
            "PRODUCT_INACTIVE" => new BadRequestObjectResult(new ApiErrorResponse { Error = "PRODUCT_INACTIVE" }),
            "INVALID_QUANTITY" => new BadRequestObjectResult(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_QUANTITY_INVALID" }),
            "INVENTORY_CONCURRENCY_CONFLICT" => new ConflictObjectResult(new ApiErrorResponse { Error = "INVENTORY_CONCURRENCY_CONFLICT" }),
            _ => new EmptyResult()
        };

        return result is not EmptyResult;
    }

    private static bool TryMapPurchaseReceiptCancelError(string error, out ActionResult result)
    {
        result = error switch
        {
            "PRODUCT_NOT_FOUND" => new NotFoundObjectResult(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" }),
            "INVALID_QUANTITY" => new BadRequestObjectResult(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_QUANTITY_INVALID" }),
            "INSUFFICIENT_STOCK" => new ConflictObjectResult(new ApiErrorResponse { Error = "PURCHASE_RECEIPT_CANCEL_INSUFFICIENT_STOCK" }),
            "INVENTORY_CONCURRENCY_CONFLICT" => new ConflictObjectResult(new ApiErrorResponse { Error = "INVENTORY_CONCURRENCY_CONFLICT" }),
            _ => new EmptyResult()
        };

        return result is not EmptyResult;
    }
}
