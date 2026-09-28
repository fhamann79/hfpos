using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IProductQueryService
{
    Task<IReadOnlyList<ProductDto>> GetLegacyCatalogAsync();

    Task<IReadOnlyList<ProductDto>> GetLookupAsync(
        string? search,
        int take,
        int? categoryId);

    Task<PagedResultDto<ProductDto>> GetPageAsync(
        string? search,
        string? status,
        int? categoryId,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir);
}
