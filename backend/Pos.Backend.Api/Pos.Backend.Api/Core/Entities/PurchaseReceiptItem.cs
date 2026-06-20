namespace Pos.Backend.Api.Core.Entities;

public class PurchaseReceiptItem
{
    public int Id { get; set; }

    public int PurchaseReceiptId { get; set; }
    public PurchaseReceipt PurchaseReceipt { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal LineTotal { get; set; }

    public decimal PreviousProductCost { get; set; }

    public decimal AppliedProductCost { get; set; }

    public string? Notes { get; set; }
}
