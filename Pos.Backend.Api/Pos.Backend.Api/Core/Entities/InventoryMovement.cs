using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class InventoryMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; }

    public InventoryMovementType Type { get; set; }

    public InventoryMovementSourceType SourceType { get; set; }

    public int? SourceId { get; set; }

    public int? SourceLineId { get; set; }

    public decimal Quantity { get; set; }

    public decimal StockBefore { get; set; }

    public decimal StockAfter { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public int UserId { get; set; }
    public User User { get; set; }

    public DateTime CreatedAt { get; set; }
}
