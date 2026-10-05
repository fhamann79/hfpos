namespace Pos.Backend.Api.Core.DTOs;

public class OpenCashSessionDto
{
    public Guid? RequestId { get; set; }
    public decimal OpeningAmount { get; set; }
    public string? OpeningNotes { get; set; }
}
