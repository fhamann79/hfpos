namespace Pos.Backend.Api.Core.DTOs;

public class CashSessionListResultDto : PagedResultDto<CashSessionListItemDto>
{
    public CashSessionSummaryDto Summary { get; set; } = new();
}

public class CashSessionSummaryDto
{
    public int OpenCount { get; set; }
    public int ClosedCount { get; set; }
}
