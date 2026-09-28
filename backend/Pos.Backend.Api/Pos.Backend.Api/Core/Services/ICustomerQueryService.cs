using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICustomerQueryService
{
    Task<IReadOnlyList<CustomerDto>> GetLookupAsync(
        string? search,
        bool? includeInactive,
        string? status,
        int? take);

    Task<PagedResultDto<CustomerDto>> GetPageAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir);
}
