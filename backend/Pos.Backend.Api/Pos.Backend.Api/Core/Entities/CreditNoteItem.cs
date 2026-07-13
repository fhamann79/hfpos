using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class CreditNoteItem
{
    public int Id { get; set; }

    public int CreditNoteId { get; set; }
    public CreditNote CreditNote { get; set; } = null!;

    public int? SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductNameSnapshot { get; set; } = string.Empty;

    public string ProductMainCodeSnapshot { get; set; } = string.Empty;

    public string? ProductAuxiliaryCodeSnapshot { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal UnitCost { get; set; }

    public decimal GrossSubtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal NetSubtotal { get; set; }

    public decimal LineSubtotal { get; set; }

    public ProductVatCategory VatCategory { get; set; }

    public decimal VatRate { get; set; }

    public decimal TaxableSubtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }

    public decimal LineCost { get; set; }
}
