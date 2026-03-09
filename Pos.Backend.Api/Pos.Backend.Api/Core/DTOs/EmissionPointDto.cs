namespace Pos.Backend.Api.Core.DTOs;

public class EmissionPointDto
{
    public int Id { get; set; }
    public int EstablishmentId { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}
