using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CreditNoteDto
{
    public int Id { get; set; }

    public int OriginalSaleId { get; set; }

    public string? OriginalSaleNumberSnapshot { get; set; }

    public string? OriginalSaleAccessKeySnapshot { get; set; }

    public string? OriginalSaleAuthorizationNumberSnapshot { get; set; }

    public DateTime? OriginalSaleAuthorizedAtSnapshot { get; set; }

    public DateTime? OriginalSaleDocumentIssuedAtSnapshot { get; set; }

    public string BuyerNameSnapshot { get; set; } = string.Empty;

    public string? BuyerIdentificationTypeSnapshot { get; set; }

    public string? BuyerIdentificationSnapshot { get; set; }

    public string? BuyerAddressSnapshot { get; set; }

    public string? BuyerEmailSnapshot { get; set; }

    public SaleDocumentStatus DocumentStatus { get; set; }

    public string? Number { get; set; }

    public string? EstablishmentCodeSnapshot { get; set; }

    public string? EmissionPointCodeSnapshot { get; set; }

    public int? Sequential { get; set; }

    public DateTime? DocumentIssuedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Notes { get; set; }

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

    public DateOnly BusinessDate { get; set; }

    public string TimeZoneIdSnapshot { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? CancellationReason { get; set; }

    public int? CancelledByUserId { get; set; }

    public string? CancelledByUsername { get; set; }

    public List<CreditNoteItemDto> Items { get; set; } = new();
}

public class CreditNoteItemDto
{
    public int Id { get; set; }

    public int? SaleItemId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductMainCode { get; set; } = string.Empty;

    public string? ProductAuxiliaryCode { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal GrossSubtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal NetSubtotal { get; set; }

    public decimal LineSubtotal { get; set; }

    public ProductVatCategory VatCategory { get; set; }

    public decimal VatRate { get; set; }

    public decimal TaxableSubtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }
}
