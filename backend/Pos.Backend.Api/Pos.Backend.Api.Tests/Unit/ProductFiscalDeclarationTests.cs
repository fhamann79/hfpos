using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class ProductFiscalDeclarationTests
{
    private static Product Product() => new() { Id = 7, CompanyId = 2, Name = "Synthetic copy", FiscalRevision = 4,
        VatCategory = ProductVatCategory.Vat15, TracksInventory = false };
    private static DeclareProductFiscalClassificationDto Request() => new() { ExpectedRevision = 3,
        ExpectedProductRevision = 3, Kind = FiscalProductKind.Service, Activity = FiscalActivity.EligibleService,
        TaxBasis = FiscalTaxBasis.GeneralRate, Confirmed = true, LegalReference = " Synthetic legal ", EvidenceReference = " Synthetic evidence " };

    [Fact]
    public void Declaration_binds_final_product_identity_without_mutating_previous_request_revision()
    {
        var dto = Request(); var product = Product(); var now = DateTime.UtcNow;
        var c = ProductFiscalDeclaration.Create(product, dto, 11, now);
        Assert.Equal(4, c.ProductFiscalRevision); Assert.Equal(4, c.Revision);
        Assert.Equal(3, dto.ExpectedProductRevision); Assert.Equal(11, c.DeclaredByUserId);
        Assert.Equal(now, c.DeclaredAt); Assert.Equal("Synthetic evidence", c.EvidenceReference);
        ProductInventoryPolicy.ValidateClassification(product, c);
    }

    [Theory]
    [InlineData(false, "Synthetic evidence")] [InlineData(true, " ")]
    public void No_confirmation_or_evidence_cannot_be_fabricated(bool confirmed, string evidence)
    {
        var dto = Request(); dto.Confirmed = confirmed; dto.EvidenceReference = evidence;
        Assert.Throws<InvalidOperationException>(() => ProductFiscalDeclaration.Create(Product(), dto, 11, DateTime.UtcNow));
    }

    [Fact]
    public void Noninventory_goods_and_incompatible_service_tax_basis_are_rejected()
    {
        var dto = Request(); dto.Kind = FiscalProductKind.Goods; dto.Activity = FiscalActivity.EligibleCommerce;
        Assert.Equal("NON_INVENTORY_REQUIRES_CURRENT_SERVICE", Assert.Throws<InvalidOperationException>(
            () => ProductFiscalDeclaration.Create(Product(), dto, 11, DateTime.UtcNow)).Message);
        dto = Request(); dto.TaxBasis = FiscalTaxBasis.NaturalAgriculturalFoodArt55_1;
        Assert.Throws<InvalidOperationException>(() => ProductFiscalDeclaration.Create(Product(), dto, 11, DateTime.UtcNow));
    }
}
