namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptCreateItemDto
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Notes { get; set; }
}
