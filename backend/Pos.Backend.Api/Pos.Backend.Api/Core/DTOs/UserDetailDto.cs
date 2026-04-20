namespace Pos.Backend.Api.Core.DTOs;

public class UserDetailDto
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool IsActive { get; set; }
    public int RoleId { get; set; }
    public string RoleCode { get; set; }
    public string RoleName { get; set; }
    public int CompanyId { get; set; }
    public int? EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
}
