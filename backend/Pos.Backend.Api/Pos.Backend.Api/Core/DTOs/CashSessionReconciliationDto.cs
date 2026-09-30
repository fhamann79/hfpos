namespace Pos.Backend.Api.Core.DTOs;

public class CashSessionReconciliationDto
{
    public decimal GrossCashSalesAmount { get; set; }
    public decimal InSessionVoidAmount { get; set; }
    public decimal NetCashSalesAmount { get; set; }
    public decimal ManualCashInAmount { get; set; }
    public decimal ManualCashOutAmount { get; set; }
    public decimal SaleVoidCashOutAmount { get; set; }
    public decimal CreditNoteRefundCashOutAmount { get; set; }
    public decimal ExpectedCashAmount { get; set; }
    public decimal? CountedCashAmount { get; set; }
    public decimal? DifferenceAmount { get; set; }
    public bool IsReconstructionComplete { get; set; }
}
