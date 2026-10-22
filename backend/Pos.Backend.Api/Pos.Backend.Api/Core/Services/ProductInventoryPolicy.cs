using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public static class ProductInventoryPolicy
{
    public static void ValidateClassification(Product product, ProductFiscalClassification? classification)
    {
        if (product.TracksInventory) return;
        if (classification is null || classification.CompanyId != product.CompanyId
            || classification.ProductId != product.Id || !classification.Confirmed
            || classification.Kind != FiscalProductKind.Service
            || classification.ProductFiscalRevision != product.FiscalRevision
            || classification.DeclaredVatCategory != product.VatCategory)
            throw new InvalidOperationException("NON_INVENTORY_REQUIRES_CURRENT_SERVICE");
        FiscalTaxResolver.ValidateClassification(product, classification);
    }
}
