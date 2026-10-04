using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPosProductLookupService
{
    Task<IReadOnlyList<PosProductLookupDto>> SearchAsync(string? search, int take, int[]? productIds = null);
}
