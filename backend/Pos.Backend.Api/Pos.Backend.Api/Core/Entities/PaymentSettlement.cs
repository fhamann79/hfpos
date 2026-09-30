using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class PaymentSettlement
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;
    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public SalePaymentMethod PaymentMethod { get; set; }
    public decimal GrossSalesAmount { get; set; }
    public decimal VoidAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal ExpectedNetAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal DifferenceAmount { get; set; }
    public Guid RequestId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public int ReconciledByUserId { get; set; }
    public User ReconciledByUser { get; set; } = null!;
    public DateTime ReconciledAt { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
}
