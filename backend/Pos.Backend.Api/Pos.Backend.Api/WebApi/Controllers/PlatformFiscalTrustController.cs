using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/platform/fiscal-trust")]
[Authorize(Policy = AppPolicies.PlatformAdmin)]
[RequirePlatformContext]
public sealed class PlatformFiscalTrustController(IPlatformFiscalTrustService trust) : ControllerBase
{
    [HttpGet]
    public Task<object> Registry() => trust.RegistryAsync();

    [HttpGet("preflight")]
    public Task<FiscalPreflightPageDto> Preflight(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => trust.PreflightAsync(page, pageSize, ct);
}
