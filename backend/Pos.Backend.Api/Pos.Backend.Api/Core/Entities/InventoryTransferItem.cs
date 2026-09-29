namespace Pos.Backend.Api.Core.Entities;

public class InventoryTransferItem
{
    public int Id { get; set; }
    public int InventoryTransferId { get; set; }
    public InventoryTransfer InventoryTransfer { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public int SourceMovementId { get; set; }
    public InventoryMovement SourceMovement { get; set; } = null!;
    public int DestinationMovementId { get; set; }
    public InventoryMovement DestinationMovement { get; set; } = null!;
}
