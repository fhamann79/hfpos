using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;

namespace Pos.Backend.Api.Core.Services;

public static class ProductFiscalDeclaration
{
    // Revisions are checked against the previous state by the transaction owner.
    public static ProductFiscalClassification Create(Product product, DeclareProductFiscalClassificationDto dto,
        int userId, DateTime now)
    {
        if (!dto.Confirmed || dto.Kind is null || dto.Activity is null || dto.TaxBasis is null
            || !ValidReference(dto.LegalReference) || !ValidReference(dto.EvidenceReference))
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_INVALID");
        var classification = new ProductFiscalClassification
        {
            CompanyId = product.CompanyId, ProductId = product.Id, Revision = checked(dto.ExpectedRevision + 1),
            ProductFiscalRevision = product.FiscalRevision, DeclaredVatCategory = product.VatCategory,
            Kind = dto.Kind.Value, Activity = dto.Activity.Value, TaxBasis = dto.TaxBasis.Value,
            LegalReference = dto.LegalReference.Trim(), EvidenceReference = dto.EvidenceReference.Trim(),
            SriMaterialCode = dto.SriMaterialCode?.Trim(),
            NaturalAgriculturalFoodConfirmed = dto.NaturalAgriculturalFoodConfirmed, FoodProcessing = dto.FoodProcessing,
            ConstructionMaterialConfirmed = dto.ConstructionMaterialConfirmed,
            DeclaredByUserId = userId, DeclaredAt = now, Confirmed = true
        };
        FiscalTaxResolver.ValidateClassification(product, classification);
        ProductInventoryPolicy.ValidateClassification(product, classification);
        return classification;
    }

    private static bool ValidReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500;

    public static ProductFiscalClassificationDto Map(ProductFiscalClassification c, Product p)
        => new(c.Revision, c.DeclaredVatCategory, c.Kind, c.Activity, c.TaxBasis, c.LegalReference, c.EvidenceReference,
            c.SriMaterialCode, c.DeclaredByUserId, c.DeclaredAt, c.Confirmed,
            c.DeclaredVatCategory == p.VatCategory && c.ProductFiscalRevision == p.FiscalRevision,
            c.NaturalAgriculturalFoodConfirmed, c.FoodProcessing, c.ConstructionMaterialConfirmed);
}
