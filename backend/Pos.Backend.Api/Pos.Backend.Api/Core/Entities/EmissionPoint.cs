using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.Entities;

public class EmissionPoint
{
    public int Id { get; set; }

    [Required]
    public int EstablishmentId { get; set; }

    public Establishment Establishment { get; set; }

    [Required]
    [MaxLength(3)]
    public string Code { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
