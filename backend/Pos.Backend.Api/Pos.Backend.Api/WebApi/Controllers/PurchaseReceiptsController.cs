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

    public PurchaseReceiptsController(
        PosDbContext context,
        IInventoryService inventoryService,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _inventoryService = inventoryService;
        _operationalContextAccessor = operationalContextAccessor;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.PurchasesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<PurchaseReceiptListItemDto>>> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.CompanyId == operationalContext.CompanyId
                && r.EstablishmentId == operationalContext.EstablishmentId);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(r => r.ReceiptDate >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(r => r.ReceiptDate <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r =>
                r.Supplier.Name.ToLower().Contains(term)
                || (r.ReceiptNumber != null && r.ReceiptNumber.ToLower().Contains(term))
                || (r.SupplierDocumentNumber != null && r.SupplierDocumentNumber.ToLower().Contains(term))
                || (r.Notes != null && r.Notes.ToLower().Contains(term)));
        }

        var receipts = await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.Id)
            .Select(r => new PurchaseReceiptListItemDto
            {
                Id = r.Id,
                SupplierId = r.SupplierId,
                SupplierName = r.Supplier.Name,
                ReceiptNumber = r.ReceiptNumber,
                SupplierDocumentNumber = r.SupplierDocumentNumber,
                ReceiptDate = r.ReceiptDate,
                Status = r.Status,
                Subtotal = r.Subtotal,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUsername = r.CreatedByUser.Username,
                PostedAt = r.PostedAt
            })
            .ToListAsync();

        return Ok(receipts);
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
                Status = r.Status,
                Subtotal = r.Subtotal,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUsername = r.CreatedByUser.Username,
                PostedAt = r.PostedAt,
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

        var supplierExists = await _context.Suppliers.AnyAsync(s =>
            s.Id == dto.SupplierId
            && s.CompanyId == operationalContext.CompanyId
            && s.IsActive);

        if (!supplierExists)
        {
            return NotFound(new ApiErrorResponse { Error = "SUPPLIER_NOT_FOUND" });
        }

        var productIds = dto.Items
            .Select(i => i.ProductId)
            .Distinct()
            .ToArray();

        var productById = await _context.Products
            .Where(p => productIds.Contains(p.Id) && p.CompanyId == operationalContext.CompanyId)
            .ToDictionaryAsync(p => p.Id);

        if (productById.Count != productIds.Length)
        {
            return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
        }

        if (productById.Values.Any(p => !p.IsActive))
        {
            return BadRequest(new ApiErrorResponse { Error = "PRODUCT_INACTIVE" });
        }

        var now = DateTime.UtcNow;
        var receiptDate = NormalizeReceiptDate(dto.ReceiptDate, now);
        var receiptItems = new List<PurchaseReceiptItem>();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            foreach (var itemDto in dto.Items)
            {
                var product = productById[itemDto.ProductId];
                var unitCost = RoundMoney(itemDto.UnitCost);
                var quantity = RoundQuantity(itemDto.Quantity);
                var lineTotal = RoundMoney(quantity * unitCost);
                var previousCost = product.Cost;

                product.Cost = unitCost;

                receiptItems.Add(new PurchaseReceiptItem
                {
                    ProductId = product.Id,
                    Quantity = quantity,
                    UnitCost = unitCost,
                    LineTotal = lineTotal,
                    PreviousProductCost = previousCost,
                    AppliedProductCost = unitCost,
                    Notes = NormalizeOptionalText(itemDto.Notes)
                });
            }

            var receipt = new PurchaseReceipt
            {
                CompanyId = operationalContext.CompanyId,
                EstablishmentId = operationalContext.EstablishmentId,
                SupplierId = dto.SupplierId,
                ReceiptNumber = NormalizeOptionalText(dto.ReceiptNumber),
                SupplierDocumentNumber = NormalizeOptionalText(dto.SupplierDocumentNumber),
                ReceiptDate = receiptDate,
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

            foreach (var item in receipt.Items)
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
                Status = r.Status,
                Subtotal = r.Subtotal,
                Notes = r.Notes,
                CreatedAt = r.CreatedAt,
                CreatedByUserId = r.CreatedByUserId,
                CreatedByUsername = r.CreatedByUser.Username,
                PostedAt = r.PostedAt,
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
                        Notes = i.Notes
                    })
                    .ToList()
            })
            .FirstAsync();
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeReceiptDate(DateTime value, DateTime fallback)
    {
        if (value == default)
        {
            return fallback;
        }

        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

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
}
