using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class ElectronicDocumentQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public ElectronicDocumentKind? Kind { get; set; }
    public SaleDocumentStatus? DocumentStatus { get; set; }
    public string? Search { get; set; }
    public bool OnlyWithSriError { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortField { get; set; } = "documentDate";
    public string? SortOrder { get; set; } = "desc";
}

public class ElectronicDocumentListItemDto
{
    public ElectronicDocumentKind Kind { get; set; }
    public int Id { get; set; }
    public string? Number { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTime? DocumentIssuedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerIdentification { get; set; }
    public string? BuyerEmail { get; set; }
    public decimal Total { get; set; }
    public SaleDocumentStatus DocumentStatus { get; set; }
    public string? AccessKey { get; set; }
    public string? AuthorizationNumber { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public int? SriEnvironment { get; set; }
    public DateTime? SriSignedAt { get; set; }
    public DateTime? SriSubmittedAt { get; set; }
    public string? SriReceptionStatus { get; set; }
    public string? SriAuthorizationStatus { get; set; }
    public string? SriLastSubmissionError { get; set; }
    public DateTime? SriLastCheckedAt { get; set; }
    public bool HasSriXmlDraft { get; set; }
    public bool HasSriSignedXml { get; set; }
    public int? OriginalSaleId { get; set; }
    public string? OriginalSaleNumber { get; set; }
}

public class ElectronicDocumentSummaryDto
{
    public int TotalDocuments { get; set; }
    public int InvoiceCount { get; set; }
    public int CreditNoteCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingAuthorizationCount { get; set; }
    public int AuthorizedCount { get; set; }
    public int RejectedCount { get; set; }
    public int CancelledCount { get; set; }
    public int WithSriErrorCount { get; set; }
}

public class ElectronicDocumentListResultDto : PagedResultDto<ElectronicDocumentListItemDto>
{
    public ElectronicDocumentSummaryDto Summary { get; set; } = new();
}

public class ElectronicDocumentDetailDto : ElectronicDocumentListItemDto
{
    public string? BuyerIdentificationType { get; set; }
    public string? BuyerAddress { get; set; }
    public int? SriEmissionType { get; set; }
    public string? SriNumericCode { get; set; }
    public DateTime? SriXmlGeneratedAt { get; set; }
    public string? SriSignatureHash { get; set; }
    public string? SriSigningCertificateThumbprint { get; set; }
    public string? SriSigningCertificateSubject { get; set; }
    public string? SriSigningCertificateSerialNumber { get; set; }
    public decimal GrossSubtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
