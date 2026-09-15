using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class CreditNoteRefund
{
    public int Id { get; set; }
    public int CreditNoteId { get; set; }
    public CreditNote CreditNote { get; set; } = null!;
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;
    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;
    public int RefundedByUserId { get; set; }
    public User RefundedByUser { get; set; } = null!;
    public SalePaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTime RefundedAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
    public int? CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }
    public int? CashMovementId { get; set; }
    public CashMovement? CashMovement { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
