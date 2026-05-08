using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class ProductUpdateDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; }
    public string? Barcode { get; set; }
    public string? InternalCode { get; set; }
    public decimal Price { get; set; }
    public ProductVatCategory? VatCategory { get; set; }
    public bool IsActive { get; set; }
}
