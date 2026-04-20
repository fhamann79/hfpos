namespace Pos.Backend.Api.Core.DTOs;

public class InventoryStockDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public decimal Quantity { get; set; }
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
}
