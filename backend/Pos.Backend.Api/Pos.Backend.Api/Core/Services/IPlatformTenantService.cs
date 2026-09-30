using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPlatformTenantService
{
    Task<PagedResultDto<PlatformTenantDto>> GetAsync(PlatformTenantQuery query);
    Task<PlatformTenantDetailDto> GetByIdAsync(int id);
    Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest request);
    Task<PlatformTenantDetailDto> SetActiveAsync(int id, bool active, TenantLifecycleRequest request);
    Task<PagedResultDto<PlatformTenantEventDto>> GetEventsAsync(int id, int page, int pageSize);
}
