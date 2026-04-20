namespace Pos.Backend.Api.Core.DTOs;

public class ProductCreateDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}
