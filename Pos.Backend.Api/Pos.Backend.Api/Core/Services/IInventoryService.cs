using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryStockListItemDto>> GetStocksAsync(string? search, int? productId, bool onlyPositive);
    Task<InventoryStockDto?> GetProductStockAsync(int productId);
    Task<PagedResultDto<InventoryMovementDto>> GetMovementsAsync(InventoryMovementQueryDto query);
    Task<InventoryMovementDto?> GetMovementByIdAsync(int id);
    Task<IReadOnlyList<InventoryMovementDto>> GetProductMovementsAsync(int productId);
    Task<InventoryMovementDto> RegisterEntryAsync(InventoryEntryDto dto);
    Task<InventoryMovementDto> RegisterExitAsync(InventoryExitDto dto);
    Task<InventoryMovementDto> RegisterAdjustmentAsync(InventoryAdjustDto dto);
    Task<InventoryMovementDto> RegisterSaleAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes);
    Task<InventoryMovementDto> RegisterVoidAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes);
}
