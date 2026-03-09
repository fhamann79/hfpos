using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EstablishmentsController : ControllerBase
{
    private readonly PosDbContext _context;

    public EstablishmentsController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<EstablishmentDto>>> Get([FromQuery] int companyId)
    {
        if (companyId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "COMPANY_ID_REQUIRED" });
        }

        var establishments = await _context.Establishments
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.Name)
            .Select(e => new EstablishmentDto
            {
                Id = e.Id,
                CompanyId = e.CompanyId,
                Name = e.Name,
                IsActive = e.IsActive
            })
            .ToListAsync();

        return Ok(establishments);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<ActionResult<EstablishmentDto>> Create([FromBody] EstablishmentCreateDto dto)
    {
        if (dto is null || dto.CompanyId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "COMPANY_ID_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var companyExists = await _context.Companies.AnyAsync(c => c.Id == dto.CompanyId);
        if (!companyExists)
        {
            return BadRequest(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        var generatedCode = await GenerateNextEstablishmentCodeAsync(dto.CompanyId);

        var establishment = new Establishment
        {
            CompanyId = dto.CompanyId,
            Code = generatedCode,
            Name = dto.Name.Trim(),
            Address = "N/A",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Establishments.Add(establishment);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = establishment.Id }, new EstablishmentDto
        {
            Id = establishment.Id,
            CompanyId = establishment.CompanyId,
            Name = establishment.Name,
            IsActive = establishment.IsActive
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<EstablishmentDto>> GetById(int id)
    {
        var establishment = await _context.Establishments
            .Where(e => e.Id == id)
            .Select(e => new EstablishmentDto
            {
                Id = e.Id,
                CompanyId = e.CompanyId,
                Name = e.Name,
                IsActive = e.IsActive
            })
            .FirstOrDefaultAsync();

        if (establishment is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        return Ok(establishment);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] EstablishmentUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var establishment = await _context.Establishments.FirstOrDefaultAsync(e => e.Id == id);
        if (establishment is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        establishment.Name = dto.Name.Trim();
        establishment.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var establishment = await _context.Establishments.FirstOrDefaultAsync(e => e.Id == id);
        if (establishment is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        var hasEmissionPoints = await _context.EmissionPoints.AnyAsync(ep => ep.EstablishmentId == id);
        if (hasEmissionPoints)
        {
            return Conflict(new ApiErrorResponse { Error = "ESTABLISHMENT_HAS_EMISSION_POINTS" });
        }

        _context.Establishments.Remove(establishment);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<string> GenerateNextEstablishmentCodeAsync(int companyId)
    {
        var numericCodes = await _context.Establishments
            .Where(e => e.CompanyId == companyId)
            .Select(e => e.Code)
            .ToListAsync();

        var maxCode = numericCodes
            .Select(code => int.TryParse(code, out var parsedCode) ? parsedCode : 0)
            .DefaultIfEmpty(0)
            .Max();

        return (maxCode + 1).ToString("D3");
    }
}
