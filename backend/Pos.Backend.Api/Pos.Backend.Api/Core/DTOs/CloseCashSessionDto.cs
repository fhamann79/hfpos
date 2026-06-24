namespace Pos.Backend.Api.Core.DTOs;

public class CloseCashSessionDto
{
    public decimal CountedCashAmount { get; set; }
    public string? ClosingNotes { get; set; }
}
