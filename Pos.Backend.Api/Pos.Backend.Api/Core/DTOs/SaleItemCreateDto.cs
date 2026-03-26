namespace Pos.Backend.Api.Core.DTOs;

public class SaleItemCreateDto
{
    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}
