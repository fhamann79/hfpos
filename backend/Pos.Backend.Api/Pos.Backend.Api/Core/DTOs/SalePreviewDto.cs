using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public sealed record SalePreviewDto(string Fingerprint, DateOnly FiscalDate, long ProfileRevision,
    decimal GrossSubtotal, decimal DiscountAmount, decimal Subtotal, decimal TaxAmount, decimal Total,
    decimal Vat15Subtotal, decimal Vat5Subtotal, decimal Vat0Subtotal, decimal VatExemptSubtotal,
    decimal VatNotSubjectSubtotal, IReadOnlyList<SalePreviewItemDto> Items);

public sealed record SalePreviewItemDto(int ProductId, decimal Quantity, decimal UnitPrice,
    decimal DiscountAmount, ProductVatCategory VatCategory, decimal VatRate,
    decimal TaxableSubtotal, decimal TaxAmount, decimal LineTotal);
