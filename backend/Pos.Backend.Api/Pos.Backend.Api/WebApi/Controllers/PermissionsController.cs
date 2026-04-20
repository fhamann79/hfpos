using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PermissionsController : ControllerBase
{
    private readonly PosDbContext _context;

    public PermissionsController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<IEnumerable<PermissionDto>>> Get()
    {
        var permissions = await _context.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PermissionDto
            {
                PermissionId = p.Id,
                Code = p.Code,
                Description = p.Description,
                Assigned = false
            })
            .ToListAsync();

        return Ok(permissions);
    }
}
