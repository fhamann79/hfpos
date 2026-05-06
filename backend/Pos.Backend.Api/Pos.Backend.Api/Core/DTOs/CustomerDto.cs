namespace Pos.Backend.Api.Core.DTOs;

public class CustomerDto
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string? Identification { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; }
}
