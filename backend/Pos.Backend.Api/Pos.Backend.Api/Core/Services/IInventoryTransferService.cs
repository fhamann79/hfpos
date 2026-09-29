using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IInventoryTransferService
{
    Task<InventoryTransferDetailDto> CreateAsync(InventoryTransferCreateDto request);
    Task<PagedResultDto<InventoryTransferListItemDto>> GetAsync(InventoryTransferQueryDto query);
    Task<InventoryTransferDetailDto?> GetByIdAsync(int id);
    Task<IReadOnlyList<InventoryTransferDestinationDto>> GetDestinationsAsync();
}
