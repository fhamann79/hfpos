using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CashMovementDto
{
    public int Id { get; set; }
    public int CashSessionId { get; set; }
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
}
