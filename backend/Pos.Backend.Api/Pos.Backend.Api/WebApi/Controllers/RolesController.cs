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
public class RolesController : ControllerBase
{
    private readonly PosDbContext _context;

    public RolesController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<IEnumerable<RoleListDto>>> Get()
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleListDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                IsActive = r.IsActive,
                PermissionsCount = r.RolePermissions.Count
            })
            .ToListAsync();

        return Ok(roles);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<RoleDetailDto>> GetById(int id)
    {
        var role = await _context.Roles
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RoleDetailDto
            {
                Id = r.Id,
                Code = r.Code,
                Name = r.Name,
                IsActive = r.IsActive,
                PermissionsCount = r.RolePermissions.Count
            })
            .FirstOrDefaultAsync();

        if (role is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        return Ok(role);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.AdminRolesWrite)]
    public async Task<ActionResult<RoleDetailDto>> Create([FromBody] RoleCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Code))
        {
            return BadRequest(new ApiErrorResponse { Error = "CODE_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var normalizedCode = dto.Code.Trim().ToUpperInvariant();
        var normalizedName = dto.Name.Trim();

        var duplicateCode = await _context.Roles.AnyAsync(r => r.Code == normalizedCode);
        if (duplicateCode)
        {
            return Conflict(new ApiErrorResponse { Error = "ROLE_CODE_ALREADY_EXISTS" });
        }

        var role = new Role
        {
            Code = normalizedCode,
            Name = normalizedName,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        var response = new RoleDetailDto
        {
            Id = role.Id,
            Code = role.Code,
            Name = role.Name,
            IsActive = role.IsActive,
            PermissionsCount = 0
        };

        return CreatedAtAction(nameof(GetById), new { id = role.Id }, response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminRolesWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] RoleUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.Name))
        {
            return BadRequest(new ApiErrorResponse { Error = "NAME_REQUIRED" });
        }

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        role.Name = dto.Name.Trim();
        role.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminRolesWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        var hasUsers = await _context.Users.AnyAsync(u => u.RoleId == id);
        if (hasUsers)
        {
            return Conflict(new ApiErrorResponse { Error = "ROLE_HAS_ASSIGNED_USERS" });
        }

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
