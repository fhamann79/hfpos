using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPlatformFiscalTrustService
{
    Task<object> RegistryAsync();
    Task<FiscalPreflightPageDto> PreflightAsync(int page, int pageSize, CancellationToken ct);
}
