using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.DTOs;

public class InventoryAdjustDto
{
    public Guid RequestId { get; set; }
    [Required]
    public int ProductId { get; set; }

    [Required]
    public decimal Quantity { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }
    public int? ExpectedMovementWatermark { get; set; }
    public decimal? ExpectedQuantity { get; set; }
    public int? ExpectedCompanyId { get; set; }
    public int? ExpectedEstablishmentId { get; set; }
}
