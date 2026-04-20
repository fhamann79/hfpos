namespace Pos.Backend.Api.Core.DTOs;

public class EstablishmentDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
}
