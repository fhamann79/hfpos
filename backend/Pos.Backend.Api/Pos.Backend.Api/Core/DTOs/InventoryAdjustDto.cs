using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.DTOs;

public class InventoryAdjustDto
{
    [Required]
    public int ProductId { get; set; }

    [Required]
    public decimal Quantity { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }
}
