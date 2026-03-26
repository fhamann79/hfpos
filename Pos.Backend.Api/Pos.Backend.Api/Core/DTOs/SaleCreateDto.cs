namespace Pos.Backend.Api.Core.DTOs;

public class SaleCreateDto
{
    public string? Notes { get; set; }

    public List<SaleItemCreateDto> Items { get; set; } = new();
}
