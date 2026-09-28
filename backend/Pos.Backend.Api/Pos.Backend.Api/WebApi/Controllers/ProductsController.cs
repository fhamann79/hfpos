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
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 200;
    private const int DefaultLookupTake = 30;
    private const int MaxLookupTake = 200;

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

    // Legacy full catalog endpoint kept temporarily for the current POS snapshot.
    // New administrative and lookup consumers must use /page or /lookup.
    [HttpGet]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> Get()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.CompanyId == operationalContext.CompanyId)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Select(p => ToDto(p))
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("lookup")]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> Lookup(
        [FromQuery] string? search,
        [FromQuery] int take = DefaultLookupTake,
        [FromQuery] int? categoryId = null)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var limit = Math.Clamp(take, 1, MaxLookupTake);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, "active", categoryId);

        var products = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(limit)
            .Select(p => ToDto(p))
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("page")]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResultDto<ProductDto>>> GetPage(
        [FromQuery] string? search,
        [FromQuery] string? status = "all",
        [FromQuery] int? categoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, status, categoryId);
        var totalItems = await query.CountAsync();
        var ordered = ApplyOrdering(query, sortBy, sortDir);

        var items = await ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(p => ToDto(p))
            .ToListAsync();

        return Ok(new PagedResultDto<ProductDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize)
        });
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

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToDto(product));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogProductsRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == id && p.CompanyId == operationalContext.CompanyId)
            .Select(p => ToDto(p))
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

    private IQueryable<Product> BuildFilteredQuery(int companyId, string? search, string? status, int? categoryId)
    {
        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId);

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        var normalizedStatus = status?.Trim().ToLowerInvariant();
        query = normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(p => p.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(p => !p.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term)
                || (p.Barcode != null && p.Barcode.ToLower().Contains(term))
                || (p.InternalCode != null && p.InternalCode.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<Product> ApplyOrdering(IQueryable<Product> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            "price" => descending
                ? query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "cost" => descending
                ? query.OrderByDescending(p => p.Cost).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.Cost).ThenBy(p => p.Id),
            "isactive" => descending
                ? query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Id)
                : query.OrderBy(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Id)
        };
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

    private static ProductDto ToDto(Product product)
        => new()
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
