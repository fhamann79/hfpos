using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class FiscalTaxResolverTests
{
    private static CompanyFiscalProfile Profile(FiscalRegime regime) => new()
    {
        CompanyId = 1, Revision = 1, Regime = regime, TaxpayerKind = TaxpayerKind.Natural,
        EffectiveFrom = new(2024, 4, 1), Confirmed = true, RimpeEligibilityConfirmed = true,
        EvidenceReference = "Synthetic declaration"
    };
    private static Product Product(ProductVatCategory category) => new() { Id = 1, CompanyId = 1, VatCategory = category };
    private static ProductFiscalClassification Classification(ProductVatCategory category, FiscalTaxBasis basis) => new()
    {
        CompanyId = 1, ProductId = 1, Revision = 1, DeclaredVatCategory = category,
        Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce, TaxBasis = basis,
        LegalReference = "Test basis", EvidenceReference = "Test evidence", Confirmed = true
    };

    [Theory]
    [InlineData(FiscalRegime.General, 0.15, "4")]
    [InlineData(FiscalRegime.Entrepreneur, 0.15, "4")]
    [InlineData(FiscalRegime.PopularBusiness, 0, "0")]
    public void Nominal_15_is_resolved_without_recategorizing_catalog(FiscalRegime regime, double expected, string code)
    {
        var product = Product(ProductVatCategory.Vat15);
        var result = FiscalTaxResolver.Resolve(Profile(regime), product,
            Classification(product.VatCategory, FiscalTaxBasis.GeneralRate), new(2026, 10, 20));
        Assert.Equal((decimal)expected, result.Rate); Assert.Equal(code, result.PercentageCode);
        Assert.Equal(ProductVatCategory.Vat15, product.VatCategory);
        Assert.Equal("2", result.TaxCode);
    }

    [Fact]
    public void Five_requires_exact_material_and_facts_not_a_hardware_store_label()
    {
        var product = Product(ProductVatCategory.Vat5);
        var classification = Classification(product.VatCategory, FiscalTaxBasis.ConstructionMaterials24_13);
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), product, classification, new(2026, 10, 20)));
        classification.SriMaterialCode = "F010999"; classification.ConstructionMaterialConfirmed = true;
        Assert.False(FiscalTaxResolver.IsSupportedMaterialCode(classification.SriMaterialCode));
        classification.SriMaterialCode = "F010202";
        var decision = FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), product, classification, new(2026, 10, 20));
        Assert.Equal(.05m, decision.Rate); Assert.Equal("5", decision.PercentageCode); Assert.Equal("F010202", decision.SriMaterialCode);
    }

    [Fact]
    public void Natural_food_zero_requires_closed_facts_and_is_not_generic_zero_permission()
    {
        var product = Product(ProductVatCategory.Vat0);
        var classification = Classification(product.VatCategory, FiscalTaxBasis.NaturalAgriculturalFoodArt55_1);
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), product, classification, new(2026, 10, 20)));
        classification.NaturalAgriculturalFoodConfirmed = true; classification.FoodProcessing = NaturalFoodProcessing.Frozen;
        Assert.Equal("0", FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), product, classification, new(2026, 10, 20)).PercentageCode);
        classification.FoodProcessing = (NaturalFoodProcessing)16;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), product, classification, new(2026, 10, 20)));
    }

    [Theory]
    [InlineData(ProductVatCategory.VatNotSubject, FiscalTaxBasis.NotSubjectArt54)]
    [InlineData(ProductVatCategory.VatExempt, FiscalTaxBasis.ExemptSpecialLaw)]
    [InlineData(ProductVatCategory.Vat0, FiscalTaxBasis.ZeroServicesArt56)]
    public void Unsupported_operation_specific_benefits_fail_closed(ProductVatCategory category, FiscalTaxBasis basis)
        => Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(Profile(FiscalRegime.General), Product(category), Classification(category, basis), new(2026, 10, 20)));

    [Fact]
    public void Compatibility_tenancy_vigency_and_classification_revisions_are_enforced()
    {
        var profile = Profile(FiscalRegime.PopularBusiness); var product = Product(ProductVatCategory.Vat15);
        var classification = Classification(product.VatCategory, FiscalTaxBasis.GeneralRate);
        profile.TaxpayerKind = TaxpayerKind.Juridical;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.ValidateProfile(profile));
        profile.TaxpayerKind = TaxpayerKind.Natural; profile.RimpeEligibilityConfirmed = false;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.ValidateProfile(profile));
        profile.RimpeEligibilityConfirmed = true; classification.Activity = FiscalActivity.Excluded;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(profile, product, classification, new(2026, 10, 20)));
        classification.Activity = FiscalActivity.EligibleCommerce; product.FiscalRevision++;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(profile, product, classification, new(2026, 10, 20)));
        product.FiscalRevision = 0; classification.CompanyId = 2;
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(profile, product, classification, new(2026, 10, 20)));
        classification.CompanyId = 1; profile.EffectiveUntil = new(2026, 10, 20);
        Assert.Throws<InvalidOperationException>(() => FiscalTaxResolver.Resolve(profile, product, classification, new(2026, 10, 20)));
    }
}
