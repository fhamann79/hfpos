namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptCreateDto
{
    public int SupplierId { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? SupplierDocumentNumber { get; set; }
    public DateTime ReceiptDate { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseReceiptCreateItemDto> Items { get; set; } = new();
}
