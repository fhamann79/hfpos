using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptListQueryDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public PurchaseReceiptStatus? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
