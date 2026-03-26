using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryStockListItemDto>> GetStocksAsync(string? search, int? productId, bool onlyPositive);
    Task<InventoryStockDto?> GetProductStockAsync(int productId);
    Task<IReadOnlyList<InventoryMovementDto>> GetProductMovementsAsync(int productId);
    Task<InventoryMovementDto> RegisterEntryAsync(InventoryEntryDto dto);
    Task<InventoryMovementDto> RegisterExitAsync(InventoryExitDto dto);
    Task<InventoryMovementDto> RegisterAdjustmentAsync(InventoryAdjustDto dto);
    Task<InventoryMovementDto> RegisterSaleAsync(InventoryExitDto dto);
    Task<InventoryMovementDto> RegisterVoidAsync(InventoryEntryDto dto);
}
