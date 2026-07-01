using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class Customer
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; }

    [MaxLength(50)]
    public string? Identification { get; set; }

    [MaxLength(2)]
    public string? IdentificationType { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(320)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
