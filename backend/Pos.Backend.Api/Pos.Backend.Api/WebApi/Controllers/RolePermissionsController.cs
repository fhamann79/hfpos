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
[Route("api/Roles/{roleId:int}/permissions")]
[Authorize]
[RequireOperationalContext]
public class RolePermissionsController : ControllerBase
{
    private readonly PosDbContext _context;

    private readonly IOperationalContextAccessor _operationalContext;
    private readonly TenantAdministrationGuard _administrationGuard;

    public RolePermissionsController(PosDbContext context, IOperationalContextAccessor operationalContext, TenantAdministrationGuard administrationGuard)
    {
        _context = context;
        _operationalContext = operationalContext;
        _administrationGuard = administrationGuard;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<IEnumerable<PermissionDto>>> Get(int roleId)
    {
        var tenant = await _operationalContext.GetRequiredContextAsync();
        var roleExists = await _context.Roles.AnyAsync(r => r.Id == roleId && r.CompanyId == tenant.CompanyId);
        if (!roleExists)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        var assignedPermissionIds = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId && rp.Role.CompanyId == tenant.CompanyId)
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
        var tenant = await _operationalContext.GetRequiredContextAsync();
        await using var tx = await _administrationGuard.BeginChangeAsync(tenant.CompanyId);
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.CompanyId == tenant.CompanyId);
        if (role is null)
        {
            return NotFound(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        var permissionIds = dto?.PermissionIds?.Distinct().ToList() ?? new List<int>();
        var requestedPermissions = await _context.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code })
            .ToListAsync();
        if (requestedPermissions.Count != permissionIds.Count)
        {
            return BadRequest(new ApiErrorResponse { Error = "INVALID_PERMISSION_IDS" });
        }

        if (role.Code == AppRoles.Admin)
        {
            var codes = requestedPermissions.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
            if (!codes.Contains(AppPermissions.AdminRolesRead)
                || !codes.Contains(AppPermissions.AdminRolesWrite))
            {
                return Conflict(new ApiErrorResponse { Error = "ADMIN_ROLE_REQUIRED_PERMISSIONS" });
            }
        }

        var existingRolePermissions = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId && rp.Role.CompanyId == tenant.CompanyId)
            .ToListAsync();

        var requestedIds = permissionIds.ToHashSet();
        var existingIds = existingRolePermissions.Select(rp => rp.PermissionId).ToHashSet();
        if (existingIds.SetEquals(requestedIds))
        {
            return NoContent();
        }

        _context.RolePermissions.RemoveRange(existingRolePermissions.Where(rp => !requestedIds.Contains(rp.PermissionId)));
        var newRolePermissions = requestedIds.Except(existingIds).Select(permissionId => new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId
        });

        await _context.RolePermissions.AddRangeAsync(newRolePermissions);
        role.AuthorizationVersion = checked(role.AuthorizationVersion + 1);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        return NoContent();
    }
}
