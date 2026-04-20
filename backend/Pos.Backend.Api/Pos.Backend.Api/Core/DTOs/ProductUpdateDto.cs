namespace Pos.Backend.Api.Core.DTOs;

public class ProductUpdateDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}
