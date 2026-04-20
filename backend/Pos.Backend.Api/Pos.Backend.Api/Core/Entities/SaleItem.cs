namespace Pos.Backend.Api.Core.Entities;

public class SaleItem
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale Sale { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineSubtotal { get; set; }
}
