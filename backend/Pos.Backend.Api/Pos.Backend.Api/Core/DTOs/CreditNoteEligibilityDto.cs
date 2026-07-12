using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CreditNoteEligibilityDto
{
    public int OriginalSaleId { get; set; }

    public string? OriginalSaleNumber { get; set; }

    public DateOnly OriginalSaleBusinessDate { get; set; }

    public DateTime? OriginalSaleDocumentIssuedAt { get; set; }

    public string? OriginalSaleAccessKey { get; set; }

    public string? OriginalSaleAuthorizationNumber { get; set; }

    public DateTime? OriginalSaleAuthorizedAt { get; set; }

    public string BuyerName { get; set; } = string.Empty;

    public string? BuyerIdentificationType { get; set; }

    public string? BuyerIdentification { get; set; }

    public string? BuyerAddress { get; set; }

    public string? BuyerEmail { get; set; }

    public decimal OriginalTotal { get; set; }

    public bool IsEligible { get; set; }

    public string? BlockingCode { get; set; }

    public string? BlockingMessage { get; set; }

    public List<CreditNoteEligibilityItemDto> Items { get; set; } = new();
}

public class CreditNoteEligibilityItemDto
{
    public int SaleItemId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public decimal SoldQuantity { get; set; }

    public decimal CreditedQuantity { get; set; }

    public decimal AvailableQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public ProductVatCategory VatCategory { get; set; }

    public decimal VatRate { get; set; }

    public decimal TaxableSubtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }
}
