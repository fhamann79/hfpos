using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

// Immutable declaration. A successor bounds the preceding effective interval without editing it.
public sealed class CompanyFiscalProfile
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public long Revision { get; set; }
    public FiscalRegime Regime { get; set; }
    public TaxpayerKind TaxpayerKind { get; set; }
    public bool IsAccountingRequired { get; set; }
    public string? SpecialTaxpayerResolution { get; set; }
    public string? WithholdingAgentResolution { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int DeclaredByUserId { get; set; }
    public DateTime DeclaredAt { get; set; }
    public bool Confirmed { get; set; }
    public bool RimpeEligibilityConfirmed { get; set; }
}
