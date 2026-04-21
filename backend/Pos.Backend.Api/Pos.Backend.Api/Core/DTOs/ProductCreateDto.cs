namespace Pos.Backend.Api.Core.DTOs;

public class ProductCreateDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; }
    public string? Barcode { get; set; }
    public string? InternalCode { get; set; }
    public decimal Price { get; set; }
}
