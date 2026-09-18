using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class ProductCostEvent
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public long Revision { get; set; }

    public decimal PreviousCost { get; set; }

    public decimal Cost { get; set; }

    public ProductCostSourceType SourceType { get; set; }

    public int? PurchaseReceiptItemId { get; set; }
    public PurchaseReceiptItem? PurchaseReceiptItem { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
