using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class RefundCreditNoteDto
{
    public SalePaymentMethod? Method { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class CreditNoteRefundDto
{
    public int Id { get; set; }
    public int CreditNoteId { get; set; }
    public SalePaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTime RefundedAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
    public int RefundedByUserId { get; set; }
    public string RefundedByUsername { get; set; } = string.Empty;
    public int? CashSessionId { get; set; }
    public int? CashMovementId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
