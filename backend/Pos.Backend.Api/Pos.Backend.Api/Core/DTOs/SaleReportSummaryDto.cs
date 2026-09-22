namespace Pos.Backend.Api.Core.DTOs;

public class SaleReportSummaryDto
{
    public int SalesCount { get; set; }
    public decimal TotalSold { get; set; }
    public decimal AuthorizedCreditNoteTotal { get; set; }
    public int AuthorizedCreditNoteCount { get; set; }
    public decimal NetTotal { get; set; }
    public decimal NetCost { get; set; }
    public decimal NetGrossProfit { get; set; }
    public decimal NetGrossMarginPercent { get; set; }
    public int InvoiceCount { get; set; }
    public int TicketCount { get; set; }
    public int VoidedCount { get; set; }
    public int AuthorizedCount { get; set; }
}
