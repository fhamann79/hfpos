using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptCsvRowDto
{
    public int Id { get; set; }
    public DateOnly ReceiptBusinessDate { get; set; }
    public string ReceiptTimeZoneIdSnapshot { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? ReceiptNumber { get; set; }
    public string? SupplierDocumentNumber { get; set; }
    public PurchaseReceiptStatus Status { get; set; }
    public decimal Subtotal { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateOnly? CanceledBusinessDate { get; set; }
    public string? CanceledTimeZoneIdSnapshot { get; set; }
    public string? CanceledByUsername { get; set; }
    public string? CancelReason { get; set; }
}
