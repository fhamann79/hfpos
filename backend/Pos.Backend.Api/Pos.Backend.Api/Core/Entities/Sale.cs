using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class Sale
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; }

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; }

    public int UserId { get; set; }
    public User User { get; set; }

    public SaleStatus Status { get; set; }

    public string? Number { get; set; }

    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }

    public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? VoidedAt { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
