namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    public decimal PreviousProductCost { get; set; }
    public decimal AppliedProductCost { get; set; }
    public string? Notes { get; set; }
}
