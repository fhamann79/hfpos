namespace Pos.Backend.Api.Core.Models;

public class SriXmlProductSnapshot
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string? InternalCode { get; set; }
}
