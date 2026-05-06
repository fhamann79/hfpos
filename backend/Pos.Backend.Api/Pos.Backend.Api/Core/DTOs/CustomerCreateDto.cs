namespace Pos.Backend.Api.Core.DTOs;

public class CustomerCreateDto
{
    public string Name { get; set; }

    public string? Identification { get; set; }

    public string? Phone { get; set; }
}
