namespace Pos.Backend.Api.Core.DTOs;

public class SaleItemDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineSubtotal { get; set; }
}
