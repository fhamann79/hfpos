namespace Pos.Backend.Api.Core.DTOs;

public class SaleCreditNoteImpactDto
{
    public int AuthorizedCreditNoteCount { get; set; }
    public decimal AuthorizedCreditNoteTotal { get; set; }
    public decimal AuthorizedCreditNoteSubtotal { get; set; }
    public decimal ReturnedCost { get; set; }
    public decimal NetTotal { get; set; }
    public decimal NetSubtotal { get; set; }
    public decimal NetCost { get; set; }
    public decimal NetGrossProfit { get; set; }
    public decimal NetGrossMarginPercent { get; set; }
}
