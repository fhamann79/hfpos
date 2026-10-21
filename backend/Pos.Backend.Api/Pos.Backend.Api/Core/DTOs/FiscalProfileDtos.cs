using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public sealed class DeclareFiscalProfileDto
{
    public long ExpectedRevision { get; set; }
    public FiscalRegime? Regime { get; set; }
    public TaxpayerKind? TaxpayerKind { get; set; }
    public bool IsAccountingRequired { get; set; }
    public string? SpecialTaxpayerResolution { get; set; }
    public string? WithholdingAgentResolution { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Confirmed { get; set; }
    public bool RimpeEligibilityConfirmed { get; set; }
}

public sealed record FiscalProfileDto(long Id, long Revision, FiscalRegime Regime,
    TaxpayerKind TaxpayerKind, bool IsAccountingRequired, string? SpecialTaxpayerResolution,
    string? WithholdingAgentResolution, DateOnly EffectiveFrom, DateOnly? EffectiveUntil,
    string EvidenceReference, string Reason, int DeclaredByUserId, DateTime DeclaredAt, bool Confirmed,
    bool RimpeEligibilityConfirmed);

public sealed class DeclareProductFiscalClassificationDto
{
    public long ExpectedRevision { get; set; }
    public long? ExpectedProductRevision { get; set; }
    public FiscalProductKind? Kind { get; set; }
    public FiscalActivity? Activity { get; set; }
    public FiscalTaxBasis? TaxBasis { get; set; }
    public string LegalReference { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string? SriMaterialCode { get; set; }
    public bool NaturalAgriculturalFoodConfirmed { get; set; }
    public NaturalFoodProcessing? FoodProcessing { get; set; }
    public bool ConstructionMaterialConfirmed { get; set; }
    public bool Confirmed { get; set; }
}

public sealed record ProductFiscalStateDto(long ProductRevision, string Name, string? InternalCode,
    string? Barcode, ProductVatCategory VatCategory, ProductFiscalClassificationDto? Classification);

public sealed record ProductFiscalClassificationDto(long Revision, ProductVatCategory DeclaredVatCategory,
    FiscalProductKind Kind, FiscalActivity Activity, FiscalTaxBasis TaxBasis, string LegalReference,
    string EvidenceReference, string? SriMaterialCode, int DeclaredByUserId, DateTime DeclaredAt,
    bool Confirmed, bool IsCurrent, bool NaturalAgriculturalFoodConfirmed,
    NaturalFoodProcessing? FoodProcessing, bool ConstructionMaterialConfirmed);
