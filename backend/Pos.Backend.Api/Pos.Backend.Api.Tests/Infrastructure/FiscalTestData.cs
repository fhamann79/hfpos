using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal static class FiscalTestData
{
    public static async Task<TestTenant> CreateFiscalTenantAsync(PostgresDatabaseFixture db, string key,
        int seed, params decimal[] stocks)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(db, key, seed, stocks);
        await using var context = db.CreateDbContext();
        await ConfirmAsync(context, tenant);
        return tenant;
    }

    public static async Task ConfirmAsync(PosDbContext db, TestTenant tenant, FiscalRegime regime = FiscalRegime.General)
    {
        if (!await db.CompanyFiscalProfiles.AnyAsync(p => p.CompanyId == tenant.CompanyId))
            db.CompanyFiscalProfiles.Add(new CompanyFiscalProfile
            {
                CompanyId = tenant.CompanyId, Revision = 1, Regime = regime, TaxpayerKind = TaxpayerKind.Natural,
                EffectiveFrom = new DateOnly(2024, 4, 1), EvidenceReference = "Synthetic test declaration only",
                Reason = "Synthetic fiscal fixture", Confirmed = true, RimpeEligibilityConfirmed = true,
                DeclaredByUserId = tenant.UserId, DeclaredAt = new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc)
            });
        var products = await db.Products.Where(p => p.CompanyId == tenant.CompanyId).ToListAsync();
        foreach (var product in products)
        {
            if (await db.ProductFiscalClassifications.AnyAsync(c => c.CompanyId == tenant.CompanyId && c.ProductId == product.Id)) continue;
            db.ProductFiscalClassifications.Add(new ProductFiscalClassification
            {
                CompanyId = tenant.CompanyId, ProductId = product.Id, Revision = 1,
                ProductFiscalRevision = product.FiscalRevision, DeclaredVatCategory = product.VatCategory,
                Kind = FiscalProductKind.Goods, Activity = FiscalActivity.EligibleCommerce,
                TaxBasis = product.VatCategory switch
                {
                    ProductVatCategory.Vat15 => FiscalTaxBasis.GeneralRate,
                    ProductVatCategory.Vat5 => FiscalTaxBasis.ConstructionMaterials24_13,
                    _ => FiscalTaxBasis.NaturalAgriculturalFoodArt55_1
                },
                NaturalAgriculturalFoodConfirmed = product.VatCategory == ProductVatCategory.Vat0,
                FoodProcessing = NaturalFoodProcessing.None,
                ConstructionMaterialConfirmed = product.VatCategory == ProductVatCategory.Vat5,
                SriMaterialCode = product.VatCategory == ProductVatCategory.Vat5 ? "F010202" : null,
                LegalReference = "Synthetic declared facts matching the test VAT category",
                EvidenceReference = "Synthetic fixture, not taxpayer data", Confirmed = true,
                DeclaredByUserId = tenant.UserId, DeclaredAt = new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc)
            });
        }
        await db.SaveChangesAsync();
    }

    public static async Task<SaleDto> CreateWithPreviewAsync(this SalesService service, SaleCreateDto request)
    {
        request.FiscalFingerprint ??= (await service.PreviewAsync(request)).Fingerprint;
        return await service.CreateAsync(request);
    }
}
