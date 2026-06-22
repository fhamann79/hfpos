using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class PurchaseReceipt
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string? ReceiptNumber { get; set; }

    public string? SupplierDocumentNumber { get; set; }

    public DateTime ReceiptDate { get; set; }

    public DateOnly ReceiptBusinessDate { get; set; }

    public string ReceiptTimeZoneIdSnapshot { get; set; } = "America/Guayaquil";

    public PurchaseReceiptStatus Status { get; set; }

    public decimal Subtotal { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateTime? PostedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    public DateOnly? CanceledBusinessDate { get; set; }

    public string? CanceledTimeZoneIdSnapshot { get; set; }

    public int? CanceledByUserId { get; set; }
    public User? CanceledByUser { get; set; }

    public string? CancelReason { get; set; }

    public List<PurchaseReceiptItem> Items { get; set; } = new();
}
