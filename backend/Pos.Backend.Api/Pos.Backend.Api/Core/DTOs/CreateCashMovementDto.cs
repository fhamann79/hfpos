using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CreateCashMovementDto
{
    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
}
