namespace Pos.Backend.Api.Core.DTOs;

public class UpdateRolePermissionsDto
{
    public List<int> PermissionIds { get; set; } = new();
}
