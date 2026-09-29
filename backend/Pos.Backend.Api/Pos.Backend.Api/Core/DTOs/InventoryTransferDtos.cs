namespace Pos.Backend.Api.Core.DTOs;

public sealed class InventoryTransferCreateDto
{
    public int DestinationEstablishmentId { get; set; }
    public Guid RequestId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public List<InventoryTransferCreateItemDto> Items { get; set; } = new();
}

public sealed class InventoryTransferCreateItemDto
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class InventoryTransferQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
}

public sealed class InventoryTransferDestinationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class InventoryTransferListItemDto
{
    public int Id { get; set; }
    public int SourceEstablishmentId { get; set; }
    public string SourceEstablishmentName { get; set; } = string.Empty;
    public int DestinationEstablishmentId { get; set; }
    public string DestinationEstablishmentName { get; set; } = string.Empty;
    public int CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
}

public sealed class InventoryTransferDetailDto : InventoryTransferListItemDto
{
    public Guid RequestId { get; set; }
    public bool WasAlreadyProcessed { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyList<InventoryTransferItemDto> Items { get; set; } = Array.Empty<InventoryTransferItemDto>();
}

public sealed class InventoryTransferItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int SourceMovementId { get; set; }
    public int DestinationMovementId { get; set; }
    public decimal SourceStockBefore { get; set; }
    public decimal SourceStockAfter { get; set; }
    public decimal DestinationStockBefore { get; set; }
    public decimal DestinationStockAfter { get; set; }
}
