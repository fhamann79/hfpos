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
[Route("api/Roles/{roleId:int}/permissions")]
[Authorize]
public class RolePermissionsController : ControllerBase
{
    private readonly PosDbContext _context;

    public RolePermissionsController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<IEnumerable<PermissionDto>>> Get(int roleId)
    {
        var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
        if (!roleExists)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        var assignedPermissionIds = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        var assignedSet = assignedPermissionIds.ToHashSet();

        var permissions = await _context.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PermissionDto
            {
                PermissionId = p.Id,
                Code = p.Code,
                Description = p.Description,
                Assigned = assignedSet.Contains(p.Id)
            })
            .ToListAsync();

        return Ok(permissions);
    }

    [HttpPut]
    [Authorize(Policy = AppPermissions.AdminRolesWrite)]
    public async Task<IActionResult> Replace(int roleId, [FromBody] UpdateRolePermissionsDto dto)
    {
        var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId);
        if (!roleExists)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        var permissionIds = dto?.PermissionIds?.Distinct().ToList() ?? new List<int>();

        var existingPermissionsCount = await _context.Permissions
            .CountAsync(p => permissionIds.Contains(p.Id));

        if (existingPermissionsCount != permissionIds.Count)
        {
            return BadRequest(new ApiErrorResponse { Error = "INVALID_PERMISSION_IDS" });
        }

        await using var tx = await _context.Database.BeginTransactionAsync();

        var existingRolePermissions = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        _context.RolePermissions.RemoveRange(existingRolePermissions);

        var newRolePermissions = permissionIds.Select(permissionId => new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId
        });

        await _context.RolePermissions.AddRangeAsync(newRolePermissions);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        return NoContent();
    }
}
