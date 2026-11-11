using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPosQuickProductService
{
    Task<PosFavoritesDto> GetFavoritesAsync(CancellationToken cancellationToken);
    Task<PosFavoritesDto> SaveFavoritesAsync(SavePosFavoritesDto dto, CancellationToken cancellationToken);
    Task<int[]> GetTopSellersAsync(CancellationToken cancellationToken);
}
