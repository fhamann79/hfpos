using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class Permission
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Code { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
