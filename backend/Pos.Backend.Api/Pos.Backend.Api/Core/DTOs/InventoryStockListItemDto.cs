namespace Pos.Backend.Api.Core.DTOs;

public class InventoryStockListItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; }
    public decimal Quantity { get; set; }
    public bool IsActive { get; set; }
}
