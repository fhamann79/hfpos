namespace Pos.Backend.Api.Core.DTOs;

public class RoleDetailDto
{
    public int Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public int PermissionsCount { get; set; }
}
