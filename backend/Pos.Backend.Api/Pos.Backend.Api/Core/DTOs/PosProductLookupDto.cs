using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public sealed class PosProductLookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? InternalCode { get; set; }
    public decimal Price { get; set; }
    public ProductVatCategory VatCategory { get; set; }
    public decimal Stock { get; set; }
    public bool IsActive { get; set; }
}
