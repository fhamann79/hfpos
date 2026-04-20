namespace Pos.Backend.Api.Core.DTOs;

public class PermissionDto
{
    public int PermissionId { get; set; }
    public string Code { get; set; }
    public string Description { get; set; }
    public bool Assigned { get; set; }
}
