using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleDto
{
    public int Id { get; set; }

    public SaleStatus Status { get; set; }

    public int? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }

    public SalePaymentMethod PaymentMethod { get; set; }

    public SaleDocumentType DocumentType { get; set; }

    public SaleDocumentStatus DocumentStatus { get; set; }

    public string? Number { get; set; }

    public string? EstablishmentCodeSnapshot { get; set; }

    public string? EmissionPointCodeSnapshot { get; set; }

    public int? Sequential { get; set; }

    public DateTime? DocumentIssuedAt { get; set; }

    public string? AccessKey { get; set; }

    public string? AuthorizationNumber { get; set; }

    public DateTime? AuthorizedAt { get; set; }

    public int? SriEnvironment { get; set; }

    public int? SriEmissionType { get; set; }

    public string? SriNumericCode { get; set; }

    public DateTime? SriXmlGeneratedAt { get; set; }

    public bool HasSriXmlDraft { get; set; }

    public DateTime? SriSignedAt { get; set; }

    public bool HasSriSignedXml { get; set; }

    public string? SriSignatureHash { get; set; }

    public string? SriSigningCertificateThumbprint { get; set; }

    public string? SriSigningCertificateSubject { get; set; }

    public string? SriSigningCertificateSerialNumber { get; set; }

    public DateTime? SriSubmittedAt { get; set; }

    public string? SriReceptionStatus { get; set; }

    public string? SriAuthorizationStatus { get; set; }

    public string? SriLastSubmissionError { get; set; }

    public DateTime? SriLastCheckedAt { get; set; }

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

    public decimal TotalCost { get; set; }

    public decimal GrossProfit { get; set; }

    public decimal GrossMarginPercent { get; set; }

    public string? Notes { get; set; }

    public int CompanyId { get; set; }

    public int EstablishmentId { get; set; }

    public int EmissionPointId { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<SaleItemDto> Items { get; set; } = new();
}
