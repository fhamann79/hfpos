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
public class EmissionPointsController : ControllerBase
{
    private readonly PosDbContext _context;

    public EmissionPointsController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<EmissionPointDto>>> Get([FromQuery] int establishmentId)
    {
        if (establishmentId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_ID_REQUIRED" });
        }

        var emissionPoints = await _context.EmissionPoints
            .Where(ep => ep.EstablishmentId == establishmentId)
            .OrderBy(ep => ep.Code)
            .Select(ep => new EmissionPointDto
            {
                Id = ep.Id,
                EstablishmentId = ep.EstablishmentId,
                Code = ep.Code,
                Name = ep.Name,
                IsActive = ep.IsActive
            })
            .ToListAsync();

        return Ok(emissionPoints);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<ActionResult<EmissionPointDto>> Create([FromBody] EmissionPointCreateDto dto)
    {
        if (dto is null || dto.EstablishmentId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_ID_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            return BadRequest(new ApiErrorResponse { Error = "CODE_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var establishmentExists = await _context.Establishments.AnyAsync(e => e.Id == dto.EstablishmentId);
        if (!establishmentExists)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        var normalizedCode = dto.Code.Trim();
        var codeExists = await _context.EmissionPoints.AnyAsync(ep =>
            ep.EstablishmentId == dto.EstablishmentId && ep.Code == normalizedCode);

        if (codeExists)
        {
            return Conflict(new ApiErrorResponse { Error = "EMISSION_POINT_CODE_ALREADY_EXISTS" });
        }

        var emissionPoint = new EmissionPoint
        {
            EstablishmentId = dto.EstablishmentId,
            Code = normalizedCode,
            Name = dto.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.EmissionPoints.Add(emissionPoint);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = emissionPoint.Id }, new EmissionPointDto
        {
            Id = emissionPoint.Id,
            EstablishmentId = emissionPoint.EstablishmentId,
            Code = emissionPoint.Code,
            Name = emissionPoint.Name,
            IsActive = emissionPoint.IsActive
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<EmissionPointDto>> GetById(int id)
    {
        var emissionPoint = await _context.EmissionPoints
            .Where(ep => ep.Id == id)
            .Select(ep => new EmissionPointDto
            {
                Id = ep.Id,
                EstablishmentId = ep.EstablishmentId,
                Code = ep.Code,
                Name = ep.Name,
                IsActive = ep.IsActive
            })
            .FirstOrDefaultAsync();

        if (emissionPoint is null)
        {
            return NotFound(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_FOUND" });
        }

        return Ok(emissionPoint);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] EmissionPointUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Code))
        {
            return BadRequest(new ApiErrorResponse { Error = "CODE_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var emissionPoint = await _context.EmissionPoints.FirstOrDefaultAsync(ep => ep.Id == id);
        if (emissionPoint is null)
        {
            return NotFound(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_FOUND" });
        }

        var normalizedCode = dto.Code.Trim();
        var codeExists = await _context.EmissionPoints.AnyAsync(ep =>
            ep.Id != id && ep.EstablishmentId == emissionPoint.EstablishmentId && ep.Code == normalizedCode);

        if (codeExists)
        {
            return Conflict(new ApiErrorResponse { Error = "EMISSION_POINT_CODE_ALREADY_EXISTS" });
        }

        emissionPoint.Code = normalizedCode;
        emissionPoint.Name = dto.Name.Trim();
        emissionPoint.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var emissionPoint = await _context.EmissionPoints.FirstOrDefaultAsync(ep => ep.Id == id);
        if (emissionPoint is null)
        {
            return NotFound(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_FOUND" });
        }

        _context.EmissionPoints.Remove(emissionPoint);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
