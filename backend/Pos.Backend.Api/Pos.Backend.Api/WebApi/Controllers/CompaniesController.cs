using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IBusinessClockService _businessClock;

    public CompaniesController(PosDbContext context, IBusinessClockService businessClock)
    {
        _context = context;
        _businessClock = businessClock;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> Get()
    {
        var companies = await _context.Companies
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
    public async Task<ActionResult<CompanyDto>> Create([FromBody] CompanyCreateDto dto)
    {
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

        var nameExists = await _context.Companies.AnyAsync(c => c.Name == normalizedName);
        if (nameExists)
        {
            return Conflict(new ApiErrorResponse { Error = "COMPANY_ALREADY_EXISTS" });
        }

        var company = new Company
        {
            Name = normalizedName,
            Ruc = $"AUTO-{Guid.NewGuid():N}"[..17],
            TimeZoneId = timeZoneValidation.TimeZoneId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = company.Id }, new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            TimeZoneId = company.TimeZoneId,
            IsActive = company.IsActive
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<CompanyDto>> GetById(int id)
    {
        var company = await _context.Companies
            .Where(c => c.Id == id)
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
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company is null)
        {
            return NotFound(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        var normalizedName = dto.Name.Trim();
        var timeZoneValidation = ValidateTimeZoneId(dto.TimeZoneId);
        if (timeZoneValidation.Result is not null)
        {
            return timeZoneValidation.Result;
        }

        var duplicateName = await _context.Companies.AnyAsync(c => c.Id != id && c.Name == normalizedName);
        if (duplicateName)
        {
            return Conflict(new ApiErrorResponse { Error = "COMPANY_ALREADY_EXISTS" });
        }

        company.Name = normalizedName;
        company.TimeZoneId = timeZoneValidation.TimeZoneId;
        company.IsActive = dto.IsActive;

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
        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company is null)
        {
            return NotFound(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        var hasEstablishments = await _context.Establishments.AnyAsync(e => e.CompanyId == id);
        if (hasEstablishments)
        {
            return Conflict(new ApiErrorResponse { Error = "COMPANY_HAS_ESTABLISHMENTS" });
        }

        _context.Companies.Remove(company);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
