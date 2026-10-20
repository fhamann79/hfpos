using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public sealed class ProductFiscalClassification
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public int ProductId { get; set; }
    public long Revision { get; set; }
    public long ProductFiscalRevision { get; set; }
    public ProductVatCategory DeclaredVatCategory { get; set; }
    public FiscalProductKind Kind { get; set; }
    public FiscalActivity Activity { get; set; }
    public FiscalTaxBasis TaxBasis { get; set; }
    public string LegalReference { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string? SriMaterialCode { get; set; }
    public bool NaturalAgriculturalFoodConfirmed { get; set; }
    public NaturalFoodProcessing? FoodProcessing { get; set; }
    public bool ConstructionMaterialConfirmed { get; set; }
    public int DeclaredByUserId { get; set; }
    public DateTime DeclaredAt { get; set; }
    public bool Confirmed { get; set; }
}
