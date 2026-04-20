using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.Security;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/authz-probe")]
public class AuthzProbeController : ControllerBase
{
    [HttpGet("admin")]
    [Authorize(Policy = AppPolicies.AdminOnly)]
    public IActionResult AdminOnly()
    {
        return Ok("OK_ADMIN");
    }

    [HttpGet("super")]
    [Authorize(Policy = AppPolicies.SupervisorOrAdmin)]
    public IActionResult SupervisorOrAdmin()
    {
        return Ok("OK_SUPERVISOR_OR_ADMIN");
    }

    [HttpGet("cashier")]
    [Authorize(Policy = AppPolicies.CashierOrAbove)]
    public IActionResult CashierOrAbove()
    {
        return Ok("OK_CASHIER_OR_ABOVE");
    }
}
