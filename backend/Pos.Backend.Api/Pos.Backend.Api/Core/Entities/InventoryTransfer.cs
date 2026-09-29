namespace Pos.Backend.Api.Core.Entities;

public class InventoryTransfer
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int SourceEstablishmentId { get; set; }
    public Establishment SourceEstablishment { get; set; } = null!;
    public int DestinationEstablishmentId { get; set; }
    public Establishment DestinationEstablishment { get; set; } = null!;
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = "America/Guayaquil";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid RequestId { get; set; }
    public List<InventoryTransferItem> Items { get; set; } = new();
}
