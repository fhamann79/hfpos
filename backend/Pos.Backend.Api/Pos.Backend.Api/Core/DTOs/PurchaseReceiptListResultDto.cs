namespace Pos.Backend.Api.Core.DTOs;

public class PurchaseReceiptListResultDto : PagedResultDto<PurchaseReceiptListItemDto>
{
    public PurchaseReceiptSummaryDto Summary { get; set; } = new();
}

public class PurchaseReceiptSummaryDto
{
    public int PostedCount { get; set; }
    public int CanceledCount { get; set; }
    public decimal TotalReceived { get; set; }
}
