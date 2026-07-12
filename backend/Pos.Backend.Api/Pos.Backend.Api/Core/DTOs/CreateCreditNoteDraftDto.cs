namespace Pos.Backend.Api.Core.DTOs;

public class CreateCreditNoteDraftDto
{
    public int OriginalSaleId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public List<CreateCreditNoteDraftItemDto> Items { get; set; } = new();
}

public class CreateCreditNoteDraftItemDto
{
    public int SaleItemId { get; set; }

    public decimal Quantity { get; set; }
}
