namespace Pos.Backend.Api.Core.DTOs;

public sealed class InventoryStockPageDto : PagedResultDto<InventoryStockListItemDto>
{
    public InventoryStockSummaryDto Summary { get; set; } = new();
}

public sealed class InventoryStockSummaryDto
{
    public int TotalProducts { get; set; }
    public int OutOfStockProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int InactiveProducts { get; set; }
    public decimal TotalInventoryUnits { get; set; }
    public decimal TotalInventoryValue { get; set; }
}

public sealed class InventoryTransferProductDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public bool IsActive { get; set; }
    public string? Barcode { get; set; }
    public string? InternalCode { get; set; }
}
