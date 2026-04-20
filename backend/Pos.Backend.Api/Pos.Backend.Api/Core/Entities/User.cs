using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class User
{
    public int Id { get; set; }

    [Required]
    public string Username { get; set; }

    [Required]
    public string Email { get; set; }

    [Required]
    public string PasswordHash { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public int RoleId { get; set; }
    public Role Role { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int? EstablishmentId { get; set; }
    public Establishment? Establishment { get; set; }

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; }
}
