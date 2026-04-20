namespace Pos.Backend.Api.Core.Models;

public class OperationalContext
{
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int UserId { get; set; }
}
