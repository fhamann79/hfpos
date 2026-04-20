using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
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

    public ProductsController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
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
                Price = p.Price,
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

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var categoryExists = await _context.Categories.AnyAsync(c =>
            c.Id == dto.CategoryId && c.CompanyId == operationalContext.CompanyId);

        if (!categoryExists)
        {
            return BadRequest(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }

        var product = new Product
        {
            CompanyId = operationalContext.CompanyId,
            CategoryId = dto.CategoryId,
            Name = dto.Name.Trim(),
            Price = dto.Price,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var response = new ProductDto
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Price = product.Price,
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
                Price = p.Price,
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

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == operationalContext.CompanyId);

        if (product is null)
        {
            return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
        }

        var categoryExists = await _context.Categories.AnyAsync(c =>
            c.Id == dto.CategoryId && c.CompanyId == operationalContext.CompanyId);

        if (!categoryExists)
        {
            return BadRequest(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }

        product.CategoryId = dto.CategoryId;
        product.Name = dto.Name.Trim();
        product.Price = dto.Price;
        product.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == operationalContext.CompanyId);

        if (product is null)
        {
            return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" });
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
