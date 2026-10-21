using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Models;

public sealed record FiscalDecision(ProductVatCategory Category, decimal Rate, string TaxCode,
    string PercentageCode, string Reason, string LegalRuleVersion, long ClassificationRevision,
    string? SriMaterialCode);

public sealed record FiscalIssuerSnapshot(long ProfileId, long ProfileRevision, FiscalRegime Regime,
    TaxpayerKind TaxpayerKind, string Name, string Ruc, string? TradeName, string? MatrixAddress,
    string? EstablishmentAddress, bool IsAccountingRequired, string? SpecialTaxpayerResolution,
    string? WithholdingAgentResolution, DateOnly FiscalDate);
