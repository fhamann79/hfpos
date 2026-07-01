using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CashSessionDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public int OpenedByUserId { get; set; }
    public string OpenedByUsername { get; set; } = string.Empty;
    public int? ClosedByUserId { get; set; }
    public string? ClosedByUsername { get; set; }
    public CashSessionStatus Status { get; set; }
    public decimal OpeningAmount { get; set; }
    public decimal ExpectedCashAmount { get; set; }
    public decimal? CountedCashAmount { get; set; }
    public decimal? DifferenceAmount { get; set; }
    public decimal CashSalesAmount { get; set; }
    public decimal CardSalesAmount { get; set; }
    public decimal TransferSalesAmount { get; set; }
    public decimal OtherSalesAmount { get; set; }
    public decimal CashInAmount { get; set; }
    public decimal CashOutAmount { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateOnly OpenBusinessDate { get; set; }
    public string OpenTimeZoneIdSnapshot { get; set; } = string.Empty;
    public DateTime? ClosedAt { get; set; }
    public DateOnly? ClosedBusinessDate { get; set; }
    public string? ClosedTimeZoneIdSnapshot { get; set; }
    public string? OpeningNotes { get; set; }
    public string? ClosingNotes { get; set; }
    public List<CashMovementDto> Movements { get; set; } = new();
}
