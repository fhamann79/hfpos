using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
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
public class CompaniesController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContext;
    private readonly IBusinessClockService _businessClock;

    public CompaniesController(PosDbContext context, IOperationalContextAccessor operationalContext, IBusinessClockService businessClock)
    {
        _context = context;
        _operationalContext = operationalContext;
        _businessClock = businessClock;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> Get()
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var companies = await _context.Companies
            .Where(c => c.Id == tenant.CompanyId)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                TimeZoneId = c.TimeZoneId,
                IsActive = c.IsActive
            })
            .ToListAsync();

        return Ok(companies);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Create()
    {
        await _operationalContext.GetRequiredContextAsync();
        return StatusCode(StatusCodes.Status403Forbidden,
            new ApiErrorResponse { Error = "PLATFORM_OPERATION_REQUIRED" });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<CompanyDto>> GetById(int id)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var company = await _context.Companies
            .Where(c => c.Id == id && c.Id == tenant.CompanyId)
            .Select(c => new CompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                TimeZoneId = c.TimeZoneId,
                IsActive = c.IsActive
            })
            .FirstOrDefaultAsync();

        if (company is null)
        {
            return NotFound(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        return Ok(company);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] CompanyUpdateDto dto)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id && c.Id == tenant.CompanyId);
        if (company is null)
        {
            return NotFound(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var normalizedName = dto.Name.Trim();
        var timeZoneValidation = ValidateTimeZoneId(dto.TimeZoneId);
        if (timeZoneValidation.Result is not null)
        {
            return timeZoneValidation.Result;
        }

        company.Name = normalizedName;
        company.TimeZoneId = timeZoneValidation.TimeZoneId;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    private (string TimeZoneId, ActionResult? Result) ValidateTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return (string.Empty, BadRequest(new ApiErrorResponse { Error = "COMPANY_TIMEZONE_REQUIRED" }));
        }

        var normalized = timeZoneId.Trim();
        if (normalized.Length > 100)
        {
            return (string.Empty, BadRequest(new ApiErrorResponse { Error = "COMPANY_TIMEZONE_INVALID" }));
        }

        try
        {
            _businessClock.ResolveTimeZone(normalized);
            return (normalized, null);
        }
        catch (InvalidOperationException)
        {
            return (string.Empty, BadRequest(new ApiErrorResponse { Error = "COMPANY_TIMEZONE_INVALID" }));
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        await _operationalContext.GetRequiredContextAsync();
        return StatusCode(StatusCodes.Status403Forbidden,
            new ApiErrorResponse { Error = "PLATFORM_OPERATION_REQUIRED" });
    }
}
