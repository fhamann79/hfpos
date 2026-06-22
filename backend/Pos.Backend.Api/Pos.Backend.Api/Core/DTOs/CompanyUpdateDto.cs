namespace Pos.Backend.Api.Core.DTOs;

public class CompanyUpdateDto
{
    public string Name { get; set; }
    public string TimeZoneId { get; set; } = "America/Guayaquil";
    public bool IsActive { get; set; }
}
