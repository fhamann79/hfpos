using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class ProductInventoryModeService
{
    // Caller holds the existing Company exclusive administration lock.
    public static async Task ApplyAsync(PosDbContext db, Product product, bool? requested,
        long? expectedClassificationRevision)
    {
        var next = requested ?? product.TracksInventory;
        if (!next && product.TracksInventory)
        {
            var classification = await db.ProductFiscalClassifications.AsNoTracking()
                .Where(c => c.CompanyId == product.CompanyId && c.ProductId == product.Id)
                .OrderByDescending(c => c.Revision).FirstOrDefaultAsync();
            if (classification is null || expectedClassificationRevision != classification.Revision)
                throw new InvalidOperationException("PRODUCT_CLASSIFICATION_REVISION_CONFLICT");
            product.TracksInventory = false;
            try { ProductInventoryPolicy.ValidateClassification(product, classification); }
            finally { product.TracksInventory = true; }
            if (await db.ProductStocks.AnyAsync(s => s.CompanyId == product.CompanyId
                && s.ProductId == product.Id && s.Quantity != 0m))
                throw new InvalidOperationException("PRODUCT_INVENTORY_MODE_HAS_STOCK");
        }
        product.TracksInventory = next;
    }
}
