using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleDto
{
    public int Id { get; set; }

    public SaleStatus Status { get; set; }

    public int? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public SalePaymentMethod PaymentMethod { get; set; }

    public SaleDocumentType DocumentType { get; set; }

    public decimal GrossSubtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Vat15Subtotal { get; set; }

    public decimal Vat5Subtotal { get; set; }

    public decimal Vat0Subtotal { get; set; }

    public decimal VatExemptSubtotal { get; set; }

    public decimal VatNotSubjectSubtotal { get; set; }

    public decimal Total { get; set; }

    public string? Notes { get; set; }

    public int CompanyId { get; set; }

    public int EstablishmentId { get; set; }

    public int EmissionPointId { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<SaleItemDto> Items { get; set; } = new();
}
