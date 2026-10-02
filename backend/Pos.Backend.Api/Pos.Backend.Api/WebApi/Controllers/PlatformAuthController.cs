using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/platform/auth")]
public sealed class PlatformAuthController(IPlatformAuthService auth, IPlatformContextAccessor accessor) : ControllerBase
{
    [HttpPost("login")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Pos.Backend.Api.Configuration.SecurityConfiguration.LoginPolicy)]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginDto request) => Ok(new { token = await auth.LoginAsync(request) });

    [HttpGet("me")]
    [Authorize(Policy = AppPolicies.PlatformAdmin)]
    [RequirePlatformContext]
    public async Task<PlatformMeDto> Me()
    {
        var context = await accessor.GetRequiredContextAsync();
        return new(context.UserId, context.Username, context.Email, PlatformClaims.AdminRole);
    }
}
