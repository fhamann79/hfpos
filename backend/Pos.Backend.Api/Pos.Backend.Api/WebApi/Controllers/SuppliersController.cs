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
    private const int DefaultPageSize = 30;

    private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISupplierQueryService _supplierQueryService;

    public SuppliersController(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISupplierQueryService supplierQueryService)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _supplierQueryService = supplierQueryService;
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
        => Ok(await _supplierQueryService.GetListAsync(search, take, activeOnly));

    [HttpGet("lookup")]
    [Authorize(Policy = AppPermissions.SuppliersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> Lookup(
        [FromQuery] string? search,
        [FromQuery] int take = DefaultLookupTake)
        => Ok(await _supplierQueryService.GetLookupAsync(search, take));

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
        => Ok(await _supplierQueryService.GetPageAsync(search, status, page, pageSize, sortBy, sortDir));

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
