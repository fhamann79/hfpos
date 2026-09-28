using System.Text.RegularExpressions;
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
public class SuppliersController : ControllerBase
{
    private const int MaxEmailLength = 320;
    private const int DefaultLookupTake = 50;
    private const int MaxLookupTake = 200;
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 200;

    private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public SuppliersController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    // Bounded lookup kept for operational selectors that expect an array.
    [HttpGet]
    [Authorize(Policy = AppPermissions.SuppliersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> Get(
        [FromQuery] string? search,
        [FromQuery] int? take = null,
        [FromQuery] bool activeOnly = false)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, activeOnly ? "active" : "all");
        var limit = Math.Clamp(take ?? DefaultLookupTake, 1, MaxLookupTake);

        var suppliers = await query
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(limit)
            .Select(s => ToDto(s))
            .ToListAsync();

        return Ok(suppliers);
    }

    [HttpGet("lookup")]
    [Authorize(Policy = AppPermissions.SuppliersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> Lookup(
        [FromQuery] string? search,
        [FromQuery] int take = DefaultLookupTake)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var limit = Math.Clamp(take, 1, MaxLookupTake);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, "active");

        var suppliers = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(limit)
            .Select(s => ToDto(s))
            .ToListAsync();

        return Ok(suppliers);
    }

    [HttpGet("page")]
    [Authorize(Policy = AppPermissions.SuppliersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResultDto<SupplierDto>>> GetPage(
        [FromQuery] string? search,
        [FromQuery] string? status = "all",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, status);
        var totalItems = await query.CountAsync();
        var ordered = ApplyOrdering(query, sortBy, sortDir);

        var items = await ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(s => ToDto(s))
            .ToListAsync();

        return Ok(new PagedResultDto<SupplierDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize)
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.SuppliersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SupplierDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var supplier = await _context.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == id && s.CompanyId == operationalContext.CompanyId)
            .Select(s => ToDto(s))
            .FirstOrDefaultAsync();

        if (supplier is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SUPPLIER_NOT_FOUND" });
        }

        return Ok(supplier);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.SuppliersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] SupplierCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "SUPPLIER_NAME_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var identification = NormalizeOptionalText(dto.Identification);
        var email = NormalizeOptionalText(dto.Email);

        if (email is not null && (email.Length > MaxEmailLength || !IsValidEmail(email)))
        {
            return BadRequest(new ApiErrorResponse { Error = "SUPPLIER_EMAIL_INVALID" });
        }

        if (await IdentificationExistsAsync(operationalContext.CompanyId, identification))
        {
            return Conflict(new ApiErrorResponse { Error = "SUPPLIER_IDENTIFICATION_ALREADY_EXISTS" });
        }

        var supplier = new Supplier
        {
            CompanyId = operationalContext.CompanyId,
            Name = dto.Name.Trim(),
            Identification = identification,
            Email = email,
            Phone = NormalizeOptionalText(dto.Phone),
            Address = NormalizeOptionalText(dto.Address),
            Notes = NormalizeOptionalText(dto.Notes),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, ToDto(supplier));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.SuppliersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, [FromBody] SupplierUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "SUPPLIER_NAME_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == operationalContext.CompanyId);

        if (supplier is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SUPPLIER_NOT_FOUND" });
        }

        var identification = NormalizeOptionalText(dto.Identification);
        var email = NormalizeOptionalText(dto.Email);

        if (email is not null && (email.Length > MaxEmailLength || !IsValidEmail(email)))
        {
            return BadRequest(new ApiErrorResponse { Error = "SUPPLIER_EMAIL_INVALID" });
        }

        if (await IdentificationExistsAsync(operationalContext.CompanyId, identification, id))
        {
            return Conflict(new ApiErrorResponse { Error = "SUPPLIER_IDENTIFICATION_ALREADY_EXISTS" });
        }

        supplier.Name = dto.Name.Trim();
        supplier.Identification = identification;
        supplier.Email = email;
        supplier.Phone = NormalizeOptionalText(dto.Phone);
        supplier.Address = NormalizeOptionalText(dto.Address);
        supplier.Notes = NormalizeOptionalText(dto.Notes);
        supplier.IsActive = dto.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.SuppliersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == operationalContext.CompanyId);

        if (supplier is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SUPPLIER_NOT_FOUND" });
        }

        supplier.IsActive = false;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<Supplier> BuildFilteredQuery(int companyId, string? search, string? status)
    {
        var query = _context.Suppliers
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId);

        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant();
        query = normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(s => s.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(s => !s.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term)
                || (s.Identification != null && s.Identification.ToLower().Contains(term))
                || (s.Email != null && s.Email.ToLower().Contains(term))
                || (s.Phone != null && s.Phone.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<Supplier> ApplyOrdering(IQueryable<Supplier> query, string? sortBy, string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(s => s.Name).ThenByDescending(s => s.Id)
                : query.OrderBy(s => s.Name).ThenBy(s => s.Id),
            "isactive" => descending
                ? query.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ThenBy(s => s.Id)
                : query.OrderBy(s => s.IsActive).ThenBy(s => s.Name).ThenBy(s => s.Id),
            "updatedat" => descending
                ? query.OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt).ThenByDescending(s => s.Id)
                : query.OrderBy(s => s.UpdatedAt ?? s.CreatedAt).ThenBy(s => s.Id),
            _ => query.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ThenBy(s => s.Id)
        };
    }

    private async Task<bool> IdentificationExistsAsync(int companyId, string? identification, int? excludedSupplierId = null)
    {
        if (identification is null)
        {
            return false;
        }

        return await _context.Suppliers.AnyAsync(s =>
            s.CompanyId == companyId
            && s.Identification == identification
            && (!excludedSupplierId.HasValue || s.Id != excludedSupplierId.Value));
    }

    private static SupplierDto ToDto(Supplier supplier)
        => new()
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Identification = supplier.Identification,
            Email = supplier.Email,
            Phone = supplier.Phone,
            Address = supplier.Address,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt,
            UpdatedAt = supplier.UpdatedAt
        };

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidEmail(string value)
        => EmailRegex.IsMatch(value);
}
