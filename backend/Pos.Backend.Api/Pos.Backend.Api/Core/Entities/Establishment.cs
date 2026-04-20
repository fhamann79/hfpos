using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class Establishment
{
    public int Id { get; set; }

    [Required]
    public int CompanyId { get; set; }

    public Company Company { get; set; }

    [Required]
    [MaxLength(3)]
    public string Code { get; set; }  // 001, 002, etc.

    [Required]
    [MaxLength(150)]
    public string Name { get; set; }

    [Required]
    [MaxLength(250)]
    public string Address { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<EmissionPoint> EmissionPoints { get; set; } = new List<EmissionPoint>();
}
