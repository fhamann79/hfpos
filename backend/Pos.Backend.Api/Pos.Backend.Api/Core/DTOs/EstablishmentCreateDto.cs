namespace Pos.Backend.Api.Core.DTOs;

public class EstablishmentCreateDto
{
    public string Name { get; set; }
    public string Code { get; set; } = "001";
    public string Address { get; set; } = string.Empty;
}
