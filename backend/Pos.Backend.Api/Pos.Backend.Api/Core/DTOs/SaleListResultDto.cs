namespace Pos.Backend.Api.Core.DTOs;

public class SaleListResultDto : PagedResultDto<SaleListItemDto>
{
    public SaleReportSummaryDto? Summary { get; set; }
}
