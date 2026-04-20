using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class Product
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; }

    public decimal Price { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
