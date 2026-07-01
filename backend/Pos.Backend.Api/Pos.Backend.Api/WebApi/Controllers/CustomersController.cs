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
public class CustomersController : ControllerBase
{
    private const int MaxNameLength = 150;
    private const int MaxIdentificationLength = 20;
    private const int MaxPhoneLength = 30;
    private const int MaxEmailLength = 320;
    private const int MaxAddressLength = 300;
    private const int MaxNotesLength = 500;
    private const int DefaultTake = 30;
    private const int MaxTake = 200;

    private static readonly HashSet<string> ValidIdentificationTypes = new(StringComparer.Ordinal)
    {
        "04",
        "05",
        "06",
        "07"
    };

    private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PassportRegex = new(@"^[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public CustomersController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.CustomersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> Get(
        [FromQuery] string? search,
        [FromQuery] bool? includeInactive,
        [FromQuery] string? status,
        [FromQuery] int? take)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.CompanyId == operationalContext.CompanyId);

        query = ApplyStatusFilter(query, includeInactive, status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || (c.Identification != null && c.Identification.ToLower().Contains(term))
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.Phone != null && c.Phone.ToLower().Contains(term))
                || (c.Address != null && c.Address.ToLower().Contains(term)));
        }

        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        var customers = await query
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Name)
            .Take(limit)
            .Select(c => ToDto(c))
            .ToListAsync();

        return Ok(customers);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CustomersRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerDto>> GetById(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var customer = await _context.Customers
            .AsNoTracking()
            .Where(c => c.Id == id && c.CompanyId == operationalContext.CompanyId)
            .Select(c => ToDto(c))
            .FirstOrDefaultAsync();

        if (customer is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CUSTOMER_NOT_FOUND" });
        }

        return Ok(customer);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.CustomersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CustomerCreateDto dto)
    {
        var input = NormalizeInput(dto);
        var validationError = ValidateInput(input);

        if (validationError is not null)
        {
            return BadRequest(new ApiErrorResponse { Error = validationError });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        if (await ActiveIdentificationExistsAsync(operationalContext.CompanyId, input.Identification, excludedCustomerId: null))
        {
            return Conflict(new ApiErrorResponse { Error = "CUSTOMER_IDENTIFICATION_DUPLICATE" });
        }

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            CompanyId = operationalContext.CompanyId,
            Name = input.Name!,
            IdentificationType = input.IdentificationType,
            Identification = input.Identification,
            Phone = input.Phone,
            Email = input.Email,
            Address = input.Address,
            Notes = input.Notes,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, ToDto(customer));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.CustomersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerUpdateDto dto)
    {
        var input = NormalizeInput(dto);
        var validationError = ValidateInput(input);

        if (validationError is not null)
        {
            return BadRequest(new ApiErrorResponse { Error = validationError });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == operationalContext.CompanyId);

        if (customer is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CUSTOMER_NOT_FOUND" });
        }

        if (dto.IsActive && await ActiveIdentificationExistsAsync(operationalContext.CompanyId, input.Identification, id))
        {
            return Conflict(new ApiErrorResponse { Error = "CUSTOMER_IDENTIFICATION_DUPLICATE" });
        }

        customer.Name = input.Name!;
        customer.IdentificationType = input.IdentificationType;
        customer.Identification = input.Identification;
        customer.Phone = input.Phone;
        customer.Email = input.Email;
        customer.Address = input.Address;
        customer.Notes = input.Notes;
        customer.IsActive = dto.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    [Authorize(Policy = AppPermissions.CustomersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == operationalContext.CompanyId);

        if (customer is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CUSTOMER_NOT_FOUND" });
        }

        if (!customer.IsActive)
        {
            return Conflict(new ApiErrorResponse { Error = "CUSTOMER_ALREADY_INACTIVE" });
        }

        customer.IsActive = false;
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = AppPermissions.CustomersWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Activate(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == operationalContext.CompanyId);

        if (customer is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CUSTOMER_NOT_FOUND" });
        }

        if (customer.IsActive)
        {
            return Conflict(new ApiErrorResponse { Error = "CUSTOMER_ALREADY_ACTIVE" });
        }

        if (await ActiveIdentificationExistsAsync(operationalContext.CompanyId, customer.Identification, id))
        {
            return Conflict(new ApiErrorResponse { Error = "CUSTOMER_IDENTIFICATION_DUPLICATE" });
        }

        customer.IsActive = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static IQueryable<Customer> ApplyStatusFilter(IQueryable<Customer> query, bool? includeInactive, string? status)
    {
        var normalizedStatus = NormalizeOptionalText(status)?.ToLowerInvariant();

        return normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(c => c.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(c => !c.IsActive),
            "all" or "todos" => query,
            _ => includeInactive == true ? query : query.Where(c => c.IsActive)
        };
    }

    private async Task<bool> ActiveIdentificationExistsAsync(int companyId, string? identification, int? excludedCustomerId)
    {
        if (identification is null)
        {
            return false;
        }

        return await _context.Customers.AnyAsync(c =>
            c.CompanyId == companyId
            && c.IsActive
            && c.Identification == identification
            && (!excludedCustomerId.HasValue || c.Id != excludedCustomerId.Value));
    }

    private static CustomerInput NormalizeInput(CustomerCreateDto? dto)
        => new(
            NormalizeRequiredText(dto?.Name),
            NormalizeOptionalText(dto?.IdentificationType),
            NormalizeOptionalText(dto?.Identification),
            NormalizeOptionalText(dto?.Phone),
            NormalizeOptionalText(dto?.Email),
            NormalizeOptionalText(dto?.Address),
            NormalizeOptionalText(dto?.Notes));

    private static CustomerInput NormalizeInput(CustomerUpdateDto? dto)
        => new(
            NormalizeRequiredText(dto?.Name),
            NormalizeOptionalText(dto?.IdentificationType),
            NormalizeOptionalText(dto?.Identification),
            NormalizeOptionalText(dto?.Phone),
            NormalizeOptionalText(dto?.Email),
            NormalizeOptionalText(dto?.Address),
            NormalizeOptionalText(dto?.Notes));

    private static string? ValidateInput(CustomerInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "CUSTOMER_NAME_REQUIRED";
        }

        if (input.Name.Length > MaxNameLength)
        {
            return "CUSTOMER_NAME_TOO_LONG";
        }

        if (input.IdentificationType is not null && !ValidIdentificationTypes.Contains(input.IdentificationType))
        {
            return "CUSTOMER_IDENTIFICATION_TYPE_INVALID";
        }

        if (input.Identification is not null && input.IdentificationType is null)
        {
            return "CUSTOMER_IDENTIFICATION_TYPE_INVALID";
        }

        if (input.IdentificationType is not null && input.Identification is null)
        {
            return "CUSTOMER_IDENTIFICATION_REQUIRED";
        }

        if (input.Identification is not null && input.Identification.Length > MaxIdentificationLength)
        {
            return "CUSTOMER_IDENTIFICATION_TOO_LONG";
        }

        var identificationError = ValidateIdentification(input.IdentificationType, input.Identification);
        if (identificationError is not null)
        {
            return identificationError;
        }

        if (input.Phone is not null && input.Phone.Length > MaxPhoneLength)
        {
            return "CUSTOMER_PHONE_TOO_LONG";
        }

        if (input.Email is not null && (input.Email.Length > MaxEmailLength || !EmailRegex.IsMatch(input.Email)))
        {
            return "CUSTOMER_EMAIL_INVALID";
        }

        if (input.Address is not null && input.Address.Length > MaxAddressLength)
        {
            return "CUSTOMER_ADDRESS_TOO_LONG";
        }

        if (input.Notes is not null && input.Notes.Length > MaxNotesLength)
        {
            return "CUSTOMER_NOTES_TOO_LONG";
        }

        return null;
    }

    private static string? ValidateIdentification(string? identificationType, string? identification)
    {
        if (identificationType is null || identification is null)
        {
            return null;
        }

        return identificationType switch
        {
            "04" => identification.Length == 13 && identification.All(char.IsDigit)
                ? null
                : "CUSTOMER_IDENTIFICATION_INVALID",
            "05" => identification.Length == 10 && identification.All(char.IsDigit)
                ? null
                : "CUSTOMER_IDENTIFICATION_INVALID",
            "06" => identification.Length <= MaxIdentificationLength && PassportRegex.IsMatch(identification)
                ? null
                : "CUSTOMER_IDENTIFICATION_INVALID",
            "07" => identification == "9999999999999"
                ? null
                : "CUSTOMER_IDENTIFICATION_INVALID",
            _ => "CUSTOMER_IDENTIFICATION_TYPE_INVALID"
        };
    }

    private static CustomerDto ToDto(Customer customer)
        => new()
        {
            Id = customer.Id,
            Name = customer.Name,
            IdentificationType = customer.IdentificationType,
            Identification = customer.Identification,
            Phone = customer.Phone,
            Email = customer.Email,
            Address = customer.Address,
            Notes = customer.Notes,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt
        };

    private static string? NormalizeRequiredText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record CustomerInput(
        string? Name,
        string? IdentificationType,
        string? Identification,
        string? Phone,
        string? Email,
        string? Address,
        string? Notes);
}
