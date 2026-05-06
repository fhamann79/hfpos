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
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public CustomersController(PosDbContext context, IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.PosSalesCreate)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> Get([FromQuery] string? search)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.CompanyId == operationalContext.CompanyId && c.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || (c.Identification != null && c.Identification.ToLower().Contains(term)));
        }

        var customers = await query
            .OrderBy(c => c.Name)
            .Take(30)
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                Name = c.Name,
                Identification = c.Identification,
                Phone = c.Phone,
                IsActive = c.IsActive
            })
            .ToListAsync();

        return Ok(customers);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.PosSalesCreate)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CustomerCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "CUSTOMER_NAME_REQUIRED" });
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var customer = new Customer
        {
            CompanyId = operationalContext.CompanyId,
            Name = dto.Name.Trim(),
            Identification = NormalizeOptionalText(dto.Identification),
            Phone = NormalizeOptionalText(dto.Phone),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var response = new CustomerDto
        {
            Id = customer.Id,
            Name = customer.Name,
            Identification = customer.Identification,
            Phone = customer.Phone,
            IsActive = customer.IsActive
        };

        return CreatedAtAction(nameof(Get), new { search = customer.Name }, response);
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
