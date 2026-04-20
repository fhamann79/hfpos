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
public class CategoriesController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public CategoriesController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.CatalogCategoriesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> Get()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var categories = await _context.Categories
            .Where(c => c.CompanyId == operationalContext.CompanyId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.CatalogCategoriesWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CategoryCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedName = dto.Name.Trim();

        var nameExists = await _context.Categories.AnyAsync(c =>
            c.CompanyId == operationalContext.CompanyId && c.Name == normalizedName);

        if (nameExists)
        {
            return Conflict(new ApiErrorResponse { Error = "CATEGORY_ALREADY_EXISTS" });
        }

        var category = new Category
        {
            CompanyId = operationalContext.CompanyId,
            Name = normalizedName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        var response = new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            IsActive = category.IsActive
        };

        return CreatedAtAction(nameof(GetById), new { id = category.Id }, response);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogCategoriesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var category = await _context.Categories
            .Where(c => c.Id == id && c.CompanyId == operationalContext.CompanyId)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                IsActive = c.IsActive
            })
            .FirstOrDefaultAsync();

        if (category is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }

        return Ok(category);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogCategoriesWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, [FromBody] CategoryUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == operationalContext.CompanyId);

        if (category is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }

        category.Name = dto.Name.Trim();
        category.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.CatalogCategoriesWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == operationalContext.CompanyId);

        if (category is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CATEGORY_NOT_FOUND" });
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
