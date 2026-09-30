using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/platform/tenants")]
[Authorize(Policy = AppPolicies.PlatformAdmin)]
[RequirePlatformContext]
public sealed class PlatformTenantsController(IPlatformTenantService tenants) : ControllerBase
{
    [HttpGet]
    public Task<PagedResultDto<PlatformTenantDto>> Get([FromQuery] PlatformTenantQuery query) => tenants.GetAsync(query);
    [HttpGet("{id:int}")]
    public Task<PlatformTenantDetailDto> GetById(int id) => tenants.GetByIdAsync(id);
    [HttpPost]
    public Task<TenantProvisionResult> Provision(TenantProvisionRequest request) => tenants.ProvisionAsync(request);
    [HttpPost("{id:int}/suspend")]
    public Task<PlatformTenantDetailDto> Suspend(int id, TenantLifecycleRequest request) => tenants.SetActiveAsync(id, false, request);
    [HttpPost("{id:int}/activate")]
    public Task<PlatformTenantDetailDto> Activate(int id, TenantLifecycleRequest request) => tenants.SetActiveAsync(id, true, request);
    [HttpGet("{id:int}/events")]
    public Task<PagedResultDto<PlatformTenantEventDto>> Events(int id, int page = 1, int pageSize = 20)
        => tenants.GetEventsAsync(id, page, pageSize);
}
