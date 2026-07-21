namespace Pos.Backend.Api.Core.DTOs;

public class SriRideDto
{
    public int? SaleId { get; set; }

    public int? CreditNoteId { get; set; }

    public string DocumentTypeLabel { get; set; } = "Factura";

    public string? DocumentNumber { get; set; }

    public string? AccessKey { get; set; }

    public SriRideQrDto? Qr { get; set; }

    public string? AuthorizationNumber { get; set; }

    public DateTime? AuthorizationDate { get; set; }

    public string? EnvironmentLabel { get; set; }

    public string? EmissionTypeLabel { get; set; }

    public DateTime? IssueDate { get; set; }

    public string TimeZoneId { get; set; } = "America/Guayaquil";

    public SriRideModifiedDocumentDto? ModifiedDocument { get; set; }

    public string? Reason { get; set; }

    public SriRideIssuerDto Issuer { get; set; } = new();

    public SriRideBuyerDto Buyer { get; set; } = new();

    public SriRideBrandingDto Branding { get; set; } = new();

    public List<SriRideItemDto> Items { get; set; } = new();

    public SriRideTotalsDto Totals { get; set; } = new();

    public List<SriRidePaymentDto> Payments { get; set; } = new();

    public List<SriRideAdditionalInfoDto> AdditionalInfo { get; set; } = new();

    public string FooterNote { get; set; } = "Representacion impresa de comprobante electronico autorizado.";
}

public class SriRideModifiedDocumentDto
{
    public string? DocumentCode { get; set; }

    public string? DocumentTypeLabel { get; set; }

    public string? DocumentNumber { get; set; }

    public DateTime? IssueDate { get; set; }
}

public class SriRideIssuerDto
{
    public string? Ruc { get; set; }

    public string? LegalName { get; set; }

    public string? TradeName { get; set; }

    public string? MatrixAddress { get; set; }

    public string? EstablishmentAddress { get; set; }

    public string? AccountingRequired { get; set; }

    public string? TaxpayerRegime { get; set; }
}

public class SriRideBuyerDto
{
    public string? IdentificationType { get; set; }

    public string? Identification { get; set; }

    public string? LegalName { get; set; }

    public string? Address { get; set; }
}

public class SriRideQrDto
{
    public string? Content { get; set; }

    public string? DataUrl { get; set; }
}

public class SriRideBrandingDto
{
    public bool LogoConfigured { get; set; }

    public string? LogoContentType { get; set; }

    public string? LogoDataUrl { get; set; }

    public string? PrimaryColor { get; set; }

    public string? DocumentFooterText { get; set; }
}

public class SriRideItemDto
{
    public string? MainCode { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal LineTotal { get; set; }
}

public class SriRideTotalsDto
{
    public decimal SubtotalWithoutTaxes { get; set; }

    public decimal TotalDiscount { get; set; }

    public decimal Vat15Subtotal { get; set; }

    public decimal Vat5Subtotal { get; set; }

    public decimal Vat0Subtotal { get; set; }

    public decimal ExemptSubtotal { get; set; }

    public decimal NotSubjectSubtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    public string Currency { get; set; } = "USD";
}

public class SriRidePaymentDto
{
    public string? PaymentMethod { get; set; }

    public decimal Amount { get; set; }
}

public class SriRideAdditionalInfoDto
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
