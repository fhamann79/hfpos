using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class InventoryStockListItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; }
    public decimal Quantity { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal UnitCost { get; set; }
    public decimal InventoryValue { get; set; }
    public StockStatus StockStatus { get; set; }
    public bool IsActive { get; set; }
}
