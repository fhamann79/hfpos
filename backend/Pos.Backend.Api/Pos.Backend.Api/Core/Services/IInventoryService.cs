using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IInventoryService
{
    Task<InventoryStockPageDto> GetStocksAsync(string? search, int? productId, bool onlyPositive, int page = 1, int pageSize = 30);
    Task<IReadOnlyList<InventoryTransferProductDto>> GetTransferLookupAsync(string? search, int take = 30);
    Task<InventoryStockDto?> GetProductStockAsync(int productId);
    Task<PagedResultDto<InventoryMovementDto>> GetMovementsAsync(InventoryMovementQueryDto query);
    Task<InventoryMovementDto?> GetMovementByIdAsync(int id);
    Task<IReadOnlyList<InventoryMovementDto>> GetProductMovementsAsync(int productId);
    Task<InventoryMovementDto> RegisterEntryAsync(InventoryEntryDto dto);
    Task<InventoryMovementDto> RegisterExitAsync(InventoryExitDto dto);
    Task<InventoryMovementDto> RegisterAdjustmentAsync(InventoryAdjustDto dto);
    Task<InventoryMovementDto> RegisterOpeningAsync(int productId, decimal quantity, int batchId, int rowNumber);
    Task<InventoryMovementDto> RegisterSaleAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes);
    Task<InventoryMovementDto> RegisterVoidAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes);
    Task<InventoryMovementDto> RegisterPurchaseReceiptAsync(int productId, decimal quantity, int purchaseReceiptId, int purchaseReceiptItemId, string? notes);
    Task<InventoryMovementDto> RegisterPurchaseReceiptCancelAsync(int productId, decimal quantity, int purchaseReceiptId, int purchaseReceiptItemId, string? notes);
    Task<InventoryMovementDto> RegisterCreditNoteReturnAsync(int productId, decimal quantity, int creditNoteId, int creditNoteItemId, string? notes);
}
