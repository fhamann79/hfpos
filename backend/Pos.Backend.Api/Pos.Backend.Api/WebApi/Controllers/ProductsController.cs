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
public class ProductsController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IMasterDataLifecycleService _lifecycle;
    private readonly TenantAdministrationGuard _administrationGuard;
    private readonly IProductCostService _productCostService;

    public ProductsController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor,
        IMasterDataLifecycleService lifecycle, TenantAdministrationGuard administrationGuard,
        IProductCostService productCostService)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _lifecycle = lifecycle;
        _administrationGuard = administrationGuard;
        _productCostService = productCostService;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> Get()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var products = await _context.Products
            .Where(p => p.CompanyId == operationalContext.CompanyId)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                Name = p.Name,
                Barcode = p.Barcode,
                InternalCode = p.InternalCode,
                Price = p.Price,
                Cost = p.Cost,
                MinimumStock = p.MinimumStock,
                VatCategory = p.VatCategory,
                IsActive = p.IsActive
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] ProductCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        if (dto.MinimumStock < 0m)
        {
            return BadRequest(new ApiErrorResponse { Error = "PRODUCT_MINIMUM_STOCK_INVALID" });
        }

        if (dto.Cost < 0m)
        {
            return BadRequest(new ApiErrorResponse { Error = "PRODUCT_COST_INVALID" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _administrationGuard.BeginChangeAsync(operationalContext.CompanyId);
        var category = await _context.Categories.SingleOrDefaultAsync(c =>
            c.Id == dto.CategoryId && c.CompanyId == operationalContext.CompanyId);

        if (category is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }
        if (!category.IsActive)
            return Conflict(new ApiErrorResponse { Error = "CATEGORY_INACTIVE" });

        var barcode = NormalizeOptionalIdentifier(dto.Barcode);
        var internalCode = NormalizeOptionalIdentifier(dto.InternalCode);
        var vatCategory = dto.VatCategory ?? ProductVatCategory.Vat15;

        if (!Enum.IsDefined(vatCategory))
        {
            return BadRequest(new ApiErrorResponse { Error = "INVALID_PRODUCT_VAT_CATEGORY" });
        }

        var duplicateIdentifierError = await ValidateUniqueIdentifiersAsync(
            operationalContext.CompanyId,
            barcode,
            internalCode);

        if (duplicateIdentifierError is not null)
        {
            return duplicateIdentifierError;
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            CompanyId = operationalContext.CompanyId,
            CategoryId = dto.CategoryId,
            Name = dto.Name.Trim(),
            Barcode = barcode,
            InternalCode = internalCode,
            Price = dto.Price,
            Cost = dto.Cost,
            MinimumStock = dto.MinimumStock,
            VatCategory = vatCategory,
            IsActive = true,
            CreatedAt = now
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        _productCostService.InitializeManualCost(product, operationalContext.UserId, now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var response = new ProductDto
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Barcode = product.Barcode,
            InternalCode = product.InternalCode,
            Price = product.Price,
            Cost = product.Cost,
            MinimumStock = product.MinimumStock,
            VatCategory = product.VatCategory,
            IsActive = product.IsActive
        };

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, response);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await _context.Products
            .Where(p => p.Id == id && p.CompanyId == operationalContext.CompanyId)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                Name = p.Name,
                Barcode = p.Barcode,
                InternalCode = p.InternalCode,
                Price = p.Price,
                Cost = p.Cost,
                MinimumStock = p.MinimumStock,
                VatCategory = p.VatCategory,
                IsActive = p.IsActive
            })
            .FirstOrDefaultAsync();

        if (product is null)
        {
            return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
        }

        return Ok(product);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, [FromBody] ProductUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        if (dto.MinimumStock < 0m)
        {
            return BadRequest(new ApiErrorResponse { Error = "PRODUCT_MINIMUM_STOCK_INVALID" });
        }

        if (dto.Cost < 0m)
        {
            return BadRequest(new ApiErrorResponse { Error = "PRODUCT_COST_INVALID" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await using var transaction = await _administrationGuard.BeginChangeAsync(operationalContext.CompanyId);
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == operationalContext.CompanyId);

        if (product is null)
        {
            return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
        }

        var category = await _context.Categories.SingleOrDefaultAsync(c =>
            c.Id == dto.CategoryId && c.CompanyId == operationalContext.CompanyId);

        if (category is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }
        if (!category.IsActive && (product.IsActive || product.CategoryId != dto.CategoryId))
            return Conflict(new ApiErrorResponse { Error = "CATEGORY_INACTIVE" });

        var barcode = NormalizeOptionalIdentifier(dto.Barcode);
        var internalCode = NormalizeOptionalIdentifier(dto.InternalCode);

        if (dto.VatCategory.HasValue && !Enum.IsDefined(dto.VatCategory.Value))
        {
            return BadRequest(new ApiErrorResponse { Error = "INVALID_PRODUCT_VAT_CATEGORY" });
        }

        var duplicateIdentifierError = await ValidateUniqueIdentifiersAsync(
            operationalContext.CompanyId,
            barcode,
            internalCode,
            id);

        if (duplicateIdentifierError is not null)
        {
            return duplicateIdentifierError;
        }

        product.CategoryId = dto.CategoryId;
        product.Name = dto.Name.Trim();
        product.Barcode = barcode;
        product.InternalCode = internalCode;
        product.Price = dto.Price;
        product.MinimumStock = dto.MinimumStock;
        product.VatCategory = dto.VatCategory ?? product.VatCategory;
        _productCostService.ApplyManualCost(
            product,
            dto.Cost,
            operationalContext.UserId,
            DateTime.UtcNow);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return NoContent();
    }

    private async Task<ObjectResult?> ValidateUniqueIdentifiersAsync(
        int companyId,
        string? barcode,
        string? internalCode,
        int? excludedProductId = null)
    {
        if (!string.IsNullOrEmpty(barcode))
        {
            var barcodeExists = await _context.Products.AnyAsync(p =>
                p.CompanyId == companyId
                && p.Barcode == barcode
                && (!excludedProductId.HasValue || p.Id != excludedProductId.Value));

            if (barcodeExists)
            {
                return Conflict(new ApiErrorResponse { Error = "PRODUCT_BARCODE_ALREADY_EXISTS" });
            }
        }

        if (!string.IsNullOrEmpty(internalCode))
        {
            var internalCodeExists = await _context.Products.AnyAsync(p =>
                p.CompanyId == companyId
                && p.InternalCode == internalCode
                && (!excludedProductId.HasValue || p.Id != excludedProductId.Value));

            if (internalCodeExists)
            {
                return Conflict(new ApiErrorResponse { Error = "PRODUCT_INTERNAL_CODE_ALREADY_EXISTS" });
            }
        }

        return null;
    }

    private static string? NormalizeOptionalIdentifier(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [HttpDelete("{id:int}")]
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        await _lifecycle.SetProductActiveAsync(operationalContext.CompanyId, id, false);

        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    public async Task<IActionResult> Activate(int id)
    {
        var tenant = await _operationalContextAccessor.GetRequiredContextAsync();
        await _lifecycle.SetProductActiveAsync(tenant.CompanyId, id, true);
        return NoContent();
    }
}
