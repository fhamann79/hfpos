using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class EmissionPointsController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContext;
    private readonly TenantAdministrationGuard _administrationGuard;
    private readonly IMasterDataLifecycleService _lifecycle;

    public EmissionPointsController(PosDbContext context, IOperationalContextAccessor operationalContext,
        TenantAdministrationGuard administrationGuard, IMasterDataLifecycleService lifecycle)
    {
        _context = context;
        _operationalContext = operationalContext;
        _administrationGuard = administrationGuard;
        _lifecycle = lifecycle;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<EmissionPointDto>>> Get([FromQuery] int establishmentId)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        if (establishmentId <= 0)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_ID_REQUIRED" });
        }

        if (!await _context.Establishments.AnyAsync(e =>
            e.Id == establishmentId && e.CompanyId == tenant.CompanyId))
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        var emissionPoints = await _context.EmissionPoints
            .Where(ep => ep.EstablishmentId == establishmentId && ep.Establishment.CompanyId == tenant.CompanyId)
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
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

        await using var tx = await _administrationGuard.BeginChangeAsync(tenant.CompanyId);
        var establishment = await _context.Establishments.SingleOrDefaultAsync(e => e.Id == dto.EstablishmentId && e.CompanyId == tenant.CompanyId);
        if (establishment is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }
        if (!establishment.IsActive)
            return Conflict(new ApiErrorResponse { Error = "ESTABLISHMENT_INACTIVE" });

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
        await tx.CommitAsync();

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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var emissionPoint = await _context.EmissionPoints
            .Where(ep => ep.Id == id && ep.Establishment.CompanyId == tenant.CompanyId)
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await using var tx = await _administrationGuard.BeginChangeAsync(tenant.CompanyId);
        var emissionPoint = await _context.EmissionPoints.FirstOrDefaultAsync(ep => ep.Id == id && ep.Establishment.CompanyId == tenant.CompanyId);
        if (emissionPoint is null)
        {
            return NotFound(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_FOUND" });
        }

        if (string.IsNullOrWhiteSpace(dto?.Code))
        {
            return BadRequest(new ApiErrorResponse { Error = "CODE_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
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

        await _context.SaveChangesAsync();
        if (!await _administrationGuard.HasActiveAdministratorAsync(tenant.CompanyId))
        {
            return Conflict(new ApiErrorResponse { Error = "LAST_ACTIVE_ADMIN_REQUIRED" });
        }
        await tx.CommitAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [HttpPost("{id:int}/deactivate")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await _lifecycle.SetEmissionPointActiveAsync(tenant.CompanyId, id, false);

        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Activate(int id)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await _lifecycle.SetEmissionPointActiveAsync(tenant.CompanyId, id, true);
        return NoContent();
    }
}
