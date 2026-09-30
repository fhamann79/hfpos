namespace Pos.Backend.Api.Core.DTOs;

public sealed record PlatformMeDto(int Id, string Username, string Email, string Role);
public sealed record PlatformCompanyInput(string Name, string Ruc, string TimeZoneId);
public sealed record PlatformEstablishmentInput(string Name, string? Address);
public sealed record PlatformEmissionPointInput(string Name);
public sealed record PlatformAdminInput(string Username, string Email, string Password);
public sealed record TenantProvisionRequest(Guid RequestId, PlatformCompanyInput Company,
    PlatformEstablishmentInput InitialEstablishment, PlatformEmissionPointInput InitialEmissionPoint,
    PlatformAdminInput InitialAdmin);
public sealed record TenantLifecycleRequest(string? Reason);
public sealed class PlatformTenantQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
public sealed class PlatformTenantDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Ruc { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed record PlatformTenantEventDto(int Id, string EventType, string PlatformUsername,
    string? Reason, DateTime CreatedAt);
public sealed record PlatformTenantDetailDto(PlatformTenantDto Company, int EstablishmentCount,
    int ActiveEstablishmentCount, int EmissionPointCount, int ActiveEmissionPointCount,
    int UserCount, int ActiveUserCount, IReadOnlyList<PlatformTenantEventDto> Events);
public sealed record TenantProvisionResult(PlatformTenantDetailDto Tenant, bool WasAlreadyProcessed);
