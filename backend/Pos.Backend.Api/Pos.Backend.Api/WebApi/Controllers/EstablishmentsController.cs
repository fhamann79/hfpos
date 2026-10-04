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
using Pos.Backend.Api.Infrastructure.Services;

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
                Code = e.Code,
                Address = e.Address,
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

        var code = dto.Code?.Trim();
        var address = dto.Address?.Trim();
        if (!OperationalIdentityProtection.ValidCode(code) || !OperationalIdentityProtection.ValidAddress(address) || dto.Name.Trim().Length > 150)
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_INPUT_INVALID" });
        await using var tx = await _administrationGuard.BeginChangeAsync(tenant.CompanyId);
        await _administrationGuard.LockOperationalWriteAsync(tenant);
        if (await _context.Establishments.AnyAsync(e => e.CompanyId == tenant.CompanyId && e.Code == code))
            return Conflict(new ApiErrorResponse { Error = "ESTABLISHMENT_CODE_ALREADY_EXISTS" });

        var establishment = new Establishment
        {
            CompanyId = tenant.CompanyId,
            Code = code!,
            Name = dto.Name.Trim(),
            Address = address!,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Establishments.Add(establishment);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        return CreatedAtAction(nameof(GetById), new { id = establishment.Id }, new EstablishmentDto
        {
            Id = establishment.Id,
            CompanyId = establishment.CompanyId,
            Name = establishment.Name,
            Code = establishment.Code,
            Address = establishment.Address,
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
                Code = e.Code,
                Address = e.Address,
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
        await _administrationGuard.LockOperationalWriteAsync(tenant);
        var establishment = await _context.Establishments.FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == tenant.CompanyId);
        if (establishment is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var code = dto.Code?.Trim() ?? establishment.Code;
        var address = dto.Address?.Trim() ?? establishment.Address;
        if (!OperationalIdentityProtection.ValidCode(code) || !OperationalIdentityProtection.ValidAddress(address) || dto.Name.Trim().Length > 150)
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_INPUT_INVALID" });
        if (code != establishment.Code && await OperationalIdentityProtection.IsUsedAsync(_context, tenant.CompanyId, id))
            return Conflict(new ApiErrorResponse { Error = "OPERATIONAL_IDENTITY_ALREADY_USED" });
        if (await _context.Establishments.AnyAsync(e => e.CompanyId == tenant.CompanyId && e.Id != id && e.Code == code))
            return Conflict(new ApiErrorResponse { Error = "ESTABLISHMENT_CODE_ALREADY_EXISTS" });
        establishment.Name = dto.Name.Trim();
        establishment.Code = code;
        establishment.Address = address;

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

    [HttpPost("{id:int}/activate")]
    [Authorize(Policy = AppPermissions.OpStructureWrite)]
    public async Task<IActionResult> Activate(int id)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await _lifecycle.SetEstablishmentActiveAsync(tenant.CompanyId, id, true);
        return NoContent();
    }
}
