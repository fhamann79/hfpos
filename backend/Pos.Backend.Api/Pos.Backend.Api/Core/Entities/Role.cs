using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class Role
{
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Code { get; set; }

    [Required]
    [MaxLength(60)]
    public string Name { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
