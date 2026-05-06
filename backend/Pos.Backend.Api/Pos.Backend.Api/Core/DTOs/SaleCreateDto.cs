using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleCreateDto
{
    public int? CustomerId { get; set; }

    public SalePaymentMethod? PaymentMethod { get; set; }

    public SaleDocumentType? DocumentType { get; set; }

    public string? Notes { get; set; }

    public List<SaleItemCreateDto> Items { get; set; } = new();
}
