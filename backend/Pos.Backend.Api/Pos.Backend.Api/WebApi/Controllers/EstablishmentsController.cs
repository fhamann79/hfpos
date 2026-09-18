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
public class EstablishmentsController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContext;
    private readonly TenantAdministrationGuard _administrationGuard;
    private readonly IMasterDataLifecycleService _lifecycle;

    public EstablishmentsController(PosDbContext context, IOperationalContextAccessor operationalContext,
        TenantAdministrationGuard administrationGuard, IMasterDataLifecycleService lifecycle)
    {
        _context = context;
        _operationalContext = operationalContext;
        _administrationGuard = administrationGuard;
        _lifecycle = lifecycle;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.OpStructureRead)]
    public async Task<ActionResult<IEnumerable<EstablishmentDto>>> Get()
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var establishments = await _context.Establishments
            .Where(e => e.CompanyId == tenant.CompanyId)
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var generatedCode = await GenerateNextEstablishmentCodeAsync(tenant.CompanyId);

        var establishment = new Establishment
        {
            CompanyId = tenant.CompanyId,
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var establishment = await _context.Establishments
            .Where(e => e.Id == id && e.CompanyId == tenant.CompanyId)
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await using var tx = await _administrationGuard.BeginChangeAsync(tenant.CompanyId);
        var establishment = await _context.Establishments.FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == tenant.CompanyId);
        if (establishment is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        establishment.Name = dto.Name.Trim();

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
        await _lifecycle.SetEstablishmentActiveAsync(tenant.CompanyId, id, false);

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

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Activate(int id)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await _lifecycle.SetEstablishmentActiveAsync(tenant.CompanyId, id, true);
        return NoContent();
    }
}
