using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SaleCreateDto
{
    public Guid RequestId { get; set; }

    public decimal? CashReceived { get; set; }

    public int? CustomerId { get; set; }

    public SalePaymentMethod? PaymentMethod { get; set; }

    public SaleDocumentType? DocumentType { get; set; }

    public decimal? DiscountAmount { get; set; }

    public string? Notes { get; set; }

    public List<SaleItemCreateDto> Items { get; set; } = new();
}
