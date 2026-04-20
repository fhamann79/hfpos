namespace Pos.Backend.Api.Core.Entities;

public class ProductStock
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; }

    public decimal Quantity { get; set; }

    public DateTime UpdatedAt { get; set; }
}
