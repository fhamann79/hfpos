namespace Pos.Backend.Api.Core.DTOs;

public class CompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string TimeZoneId { get; set; } = "America/Guayaquil";
    public bool IsActive { get; set; }
}
