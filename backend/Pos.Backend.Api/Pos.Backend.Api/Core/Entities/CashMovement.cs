using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class CashMovement
{
    public int Id { get; set; }

    public int CashSessionId { get; set; }
    public CashSession CashSession { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; }

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; }

    public int UserId { get; set; }
    public User User { get; set; }

    public CashMovementType Type { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateOnly BusinessDate { get; set; }

    public string TimeZoneIdSnapshot { get; set; } = "America/Guayaquil";
}
