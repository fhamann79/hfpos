using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class InventoryMovementQueryDto
{
    public int? ProductId { get; set; }
    public InventoryMovementType? Type { get; set; }
    public InventoryMovementSourceType? SourceType { get; set; }
    public int? SourceId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? UserId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
