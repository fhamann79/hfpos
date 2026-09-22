using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleListQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public SaleStatus? Status { get; set; }
    public string? Search { get; set; }
    public int? UserId { get; set; }
    public SaleDocumentType? DocumentType { get; set; }
    public SaleDocumentStatus? DocumentStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool IncludeSummary { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}
