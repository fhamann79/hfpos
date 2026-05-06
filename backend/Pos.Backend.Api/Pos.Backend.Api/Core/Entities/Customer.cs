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

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
