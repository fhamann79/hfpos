namespace Pos.Backend.Api.Core.DTOs;

public class UserCreateDto
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public int RoleId { get; set; }
    public int CompanyId { get; set; }
    public int? EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public bool IsActive { get; set; }
}
