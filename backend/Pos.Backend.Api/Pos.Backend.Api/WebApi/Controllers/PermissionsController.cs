using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Core.Services;

using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class PermissionsController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContext;

    public PermissionsController(PosDbContext context, IOperationalContextAccessor operationalContext)
    {
        _context = context;
        _operationalContext = operationalContext;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminRolesRead)]
    public async Task<ActionResult<IEnumerable<PermissionDto>>> Get()
    {
        await _operationalContext.GetRequiredContextAsync();
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
