using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISupplierQueryService
{
    Task<IReadOnlyList<SupplierDto>> GetListAsync(
        string? search,
        int? take,
        bool activeOnly);

    Task<IReadOnlyList<SupplierDto>> GetLookupAsync(
        string? search,
        int take);

    Task<PagedResultDto<SupplierDto>> GetPageAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir);
}
