namespace Pos.Backend.Api.Core.DTOs;

public class UserUpdateDto
{
    public string Email { get; set; }
    public int RoleId { get; set; }
    public int CompanyId { get; set; }
    public int? EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public bool IsActive { get; set; }
}
