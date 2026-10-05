using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/account/recovery")]
[EnableRateLimiting(SecurityConfiguration.LoginPolicy)]
public sealed class PasswordRecoveryController(PasswordRecoveryService service) : ControllerBase
{
    [HttpPost("users/{id:int}")]
    [Authorize(Policy = AppPermissions.UsersRecoveryManage)]
    [RequireOperationalContext]
    public Task<RecoveryIssuedDto> Issue(int id, IssueRecoveryDto request) => service.IssueAsync(false, id, request);

    [HttpGet("users/{id:int}")]
    [Authorize(Policy = AppPermissions.UsersRecoveryManage)]
    [RequireOperationalContext]
    public Task<IReadOnlyList<RecoveryStatusDto>> Status(int id) => service.StatusAsync(false, id);

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = AppPermissions.UsersRecoveryManage)]
    [RequireOperationalContext]
    public async Task<IActionResult> Revoke(Guid id) { await service.RevokeAsync(false, id, null); return NoContent(); }

    [HttpPost("complete")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityConfiguration.LoginPolicy)]
    public async Task<IActionResult> Complete(CompleteRecoveryDto request) { await service.CompleteAsync(false, request); return NoContent(); }

    [HttpPut("password")]
    [Authorize]
    [RequireOperationalContext]
    public async Task<IActionResult> Change(SelfPasswordChangeDto request) { await service.ChangeSelfAsync(false, request); return NoContent(); }
}

[ApiController]
[Route("api/platform/account/recovery")]
[EnableRateLimiting(SecurityConfiguration.LoginPolicy)]
public sealed class PlatformPasswordRecoveryController(PasswordRecoveryService service) : ControllerBase
{
    [HttpGet("users")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public Task<IReadOnlyList<RecoveryAccountDto>> Accounts() => service.PlatformAccountsAsync();

    [HttpPost("users/{id:int}")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public Task<RecoveryIssuedDto> Issue(int id, IssueRecoveryDto request) => service.IssueAsync(true, id, request);

    [HttpGet("users/{id:int}")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public Task<IReadOnlyList<RecoveryStatusDto>> Status(int id) => service.StatusAsync(true, id);

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public async Task<IActionResult> Revoke(Guid id, SelfPasswordChangeDto request)
    { await service.RevokeAsync(true, id, request.CurrentPassword); return NoContent(); }

    [HttpPost("complete")]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityConfiguration.LoginPolicy)]
    public async Task<IActionResult> Complete(CompleteRecoveryDto request) { await service.CompleteAsync(true, request); return NoContent(); }

    [HttpPut("password")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public async Task<IActionResult> Change(SelfPasswordChangeDto request) { await service.ChangeSelfAsync(true, request); return NoContent(); }
}
