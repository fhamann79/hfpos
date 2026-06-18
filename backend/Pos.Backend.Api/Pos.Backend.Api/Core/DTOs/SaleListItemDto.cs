using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleListItemDto
{
    public int Id { get; set; }

    public SaleStatus Status { get; set; }

    public string? Number { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerIdentification { get; set; }

    public SaleDocumentType DocumentType { get; set; }

    public SaleDocumentStatus DocumentStatus { get; set; }

    public string? AccessKey { get; set; }

    public bool HasSriXmlDraft { get; set; }

    public DateTime? SriSignedAt { get; set; }

    public bool HasSriSignedXml { get; set; }

    public DateTime? SriSubmittedAt { get; set; }

    public string? SriReceptionStatus { get; set; }

    public string? SriAuthorizationStatus { get; set; }

    public string? SriLastSubmissionError { get; set; }

    public DateTime? SriLastCheckedAt { get; set; }

    public decimal Total { get; set; }

    public decimal TotalCost { get; set; }

    public decimal GrossProfit { get; set; }

    public decimal GrossMarginPercent { get; set; }

    public int ItemsCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public int UserId { get; set; }

    public string Username { get; set; }

    public string? Notes { get; set; }
}
