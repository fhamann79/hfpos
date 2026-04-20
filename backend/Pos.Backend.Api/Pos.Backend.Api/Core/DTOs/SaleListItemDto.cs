using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleListItemDto
{
    public int Id { get; set; }

    public SaleStatus Status { get; set; }

    public decimal Total { get; set; }

    public int ItemsCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public int UserId { get; set; }

    public string Username { get; set; }

    public string? Notes { get; set; }
}
