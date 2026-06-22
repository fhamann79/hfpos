using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptListItemDto
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? ReceiptNumber { get; set; }
    public string? SupplierDocumentNumber { get; set; }
    public DateTime ReceiptDate { get; set; }
    public DateOnly ReceiptBusinessDate { get; set; }
    public string ReceiptTimeZoneIdSnapshot { get; set; } = string.Empty;
    public PurchaseReceiptStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateOnly? CanceledBusinessDate { get; set; }
    public string? CanceledTimeZoneIdSnapshot { get; set; }
    public int? CanceledByUserId { get; set; }
    public string? CanceledByUsername { get; set; }
    public string? CancelReason { get; set; }
}
