using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public static class FiscalTaxResolver
{
    public const string RuleVersion = "EC-LRTI-20260401-FT2.34-v1";

    public static void ValidateProfile(CompanyFiscalProfile profile)
    {
        if (!profile.Confirmed || !Enum.IsDefined(profile.Regime) || !Enum.IsDefined(profile.TaxpayerKind)
            || string.IsNullOrWhiteSpace(profile.EvidenceReference))
            throw new InvalidOperationException("FISCAL_PROFILE_CONFIRMATION_REQUIRED");
        if (profile.Regime == FiscalRegime.PopularBusiness && profile.TaxpayerKind != TaxpayerKind.Natural)
            throw new InvalidOperationException("FISCAL_PROFILE_INCOMPATIBLE");
        if (profile.TaxpayerKind == TaxpayerKind.Juridical && !profile.IsAccountingRequired)
            throw new InvalidOperationException("FISCAL_PROFILE_INCOMPATIBLE");
        if (profile.Regime != FiscalRegime.General && !profile.RimpeEligibilityConfirmed)
            throw new InvalidOperationException("RIMPE_ELIGIBILITY_CONFIRMATION_REQUIRED");
    }

    public static FiscalDecision Resolve(CompanyFiscalProfile profile, Product product,
        ProductFiscalClassification? classification, DateOnly fiscalDate)
    {
        ValidateProfile(profile);
        if (fiscalDate < new DateOnly(2024, 4, 1))
            throw new InvalidOperationException("FISCAL_DATE_RULE_UNSUPPORTED");
        if (fiscalDate < profile.EffectiveFrom || (profile.EffectiveUntil is { } until && fiscalDate >= until))
            throw new InvalidOperationException("FISCAL_PROFILE_NOT_EFFECTIVE");
        if (classification is null || classification.CompanyId != profile.CompanyId
            || classification.ProductId != product.Id || product.CompanyId != profile.CompanyId)
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_REQUIRED");
        ValidateClassification(product, classification);
        if (profile.Regime != FiscalRegime.General && classification.Activity == FiscalActivity.Excluded)
            throw new InvalidOperationException("RIMPE_EXCLUDED_ACTIVITY_UNSUPPORTED");

        var category = product.VatCategory;
        var reason = classification.TaxBasis == FiscalTaxBasis.NaturalAgriculturalFoodArt55_1
            ? "LRTI 55.1: alimento agricola natural sin mezcla ni preparacion declaradas"
            : classification.TaxBasis == FiscalTaxBasis.ConstructionMaterials24_13
                ? "LRTI art. innumerado posterior a 55; NAC-DGERCGC24-00000013; ficha anexo 23"
                : "LRTI 52/56/65: operacion gravada ordinaria";
        if (profile.Regime == FiscalRegime.PopularBusiness)
        {
            category = ProductVatCategory.Vat0;
            reason = "LRTI 97.3/97.4/97.9: operacion comprendida declarada de negocio popular";
        }
        var (rate, code) = category switch
        {
            ProductVatCategory.Vat15 => (0.15m, "4"),
            ProductVatCategory.Vat5 => (0.05m, "5"),
            ProductVatCategory.Vat0 => (0m, "0"),
            _ => throw new InvalidOperationException("FISCAL_OPERATION_FACTS_UNSUPPORTED")
        };
        return new FiscalDecision(category, rate, "2", code, reason, RuleVersion,
            classification.Revision, classification.SriMaterialCode);
    }

    public static void ValidateClassification(Product product, ProductFiscalClassification classification)
    {
        if (!classification.Confirmed || classification.DeclaredVatCategory != product.VatCategory
            || classification.ProductFiscalRevision != product.FiscalRevision)
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_REQUIRED");
        if (!Enum.IsDefined(classification.Kind) || !Enum.IsDefined(classification.Activity)
            || !Enum.IsDefined(classification.TaxBasis) || string.IsNullOrWhiteSpace(classification.LegalReference)
            || string.IsNullOrWhiteSpace(classification.EvidenceReference))
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_INVALID");
        if (classification.Activity == FiscalActivity.Undetermined)
            throw new InvalidOperationException("FISCAL_ACTIVITY_UNDETERMINED");
        if ((classification.Kind == FiscalProductKind.Goods && classification.Activity == FiscalActivity.EligibleService)
            || (classification.Kind == FiscalProductKind.Service && classification.Activity == FiscalActivity.EligibleCommerce))
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_INVALID");

        var expectedCategory = classification.TaxBasis switch
        {
            FiscalTaxBasis.GeneralRate => ProductVatCategory.Vat15,
            FiscalTaxBasis.NaturalAgriculturalFoodArt55_1 when classification.Kind == FiscalProductKind.Goods
                && classification.NaturalAgriculturalFoodConfirmed && classification.FoodProcessing.HasValue
                && ((int)classification.FoodProcessing.Value & ~15) == 0 => ProductVatCategory.Vat0,
            FiscalTaxBasis.ConstructionMaterials24_13 when classification.Kind == FiscalProductKind.Goods => ProductVatCategory.Vat5,
            FiscalTaxBasis.NotSubjectArt54 or FiscalTaxBasis.ExemptSpecialLaw =>
                throw new InvalidOperationException("FISCAL_OPERATION_FACTS_UNSUPPORTED"),
            _ => throw new InvalidOperationException("FISCAL_LEGAL_BASIS_UNSUPPORTED")
        };
        if (expectedCategory != product.VatCategory)
            throw new InvalidOperationException("FISCAL_CLASSIFICATION_CONFLICT");

        if (expectedCategory == ProductVatCategory.Vat5
            && (!classification.ConstructionMaterialConfirmed || !IsSupportedMaterialCode(classification.SriMaterialCode)))
            throw new InvalidOperationException("SRI_MATERIAL_CODE_REQUIRED");
        if (expectedCategory != ProductVatCategory.Vat5 && classification.SriMaterialCode is not null)
            throw new InvalidOperationException("FISCAL_CLASSIFICATION_CONFLICT");
    }

    public static bool IsSupportedMaterialCode(string? code) => code is
        "F010101" or "F010201" or "F010202" or "F010203" or "F010204" or "F010205"
        or "F010301" or "F010401" or "F010402" or "F010501" or "F010601"
        or "F010701" or "F010702" or "F010703" or "F010801" or "F010802" or "F010803" or "F010804";
}
