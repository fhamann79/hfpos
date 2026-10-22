using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalProfileService(PosDbContext db, IOperationalContextAccessor accessor,
    IBusinessClockService clock, ISriFiscalClock fiscalClock) : IFiscalProfileService
{
    public async Task<IReadOnlyList<FiscalProfileDto>> GetHistoryAsync()
    {
        var context = await accessor.GetRequiredContextAsync();
        var profiles = await db.CompanyFiscalProfiles.AsNoTracking().Where(p => p.CompanyId == context.CompanyId)
            .OrderByDescending(p => p.Revision).ToListAsync();
        return profiles.Select((p, index) => Map(p, index == 0 ? null : profiles[index - 1].EffectiveFrom)).ToList();
    }

    public async Task<FiscalProfileDto> DeclareAsync(DeclareFiscalProfileDto dto)
    {
        var context = await accessor.GetRequiredContextAsync();
        var guard = new TenantAdministrationGuard(db);
        await using var tx = await guard.BeginChangeAsync(context.CompanyId);
        await guard.LockExclusiveOperationalWriteAsync(context);
        await RequirePermissionAsync(context);
        var latest = await db.CompanyFiscalProfiles.AsNoTracking().Where(p => p.CompanyId == context.CompanyId)
            .OrderByDescending(p => p.Revision).FirstOrDefaultAsync();
        if ((latest?.Revision ?? 0) != dto.ExpectedRevision)
            throw new InvalidOperationException("FISCAL_PROFILE_REVISION_CONFLICT");
        var now = clock.UtcNow;
        var today = fiscalClock.GetEcuadorFiscalDate(now);
        if (dto.EffectiveFrom < today || (latest is not null && dto.EffectiveFrom < latest.EffectiveFrom)
            || (dto.EffectiveUntil.HasValue && dto.EffectiveUntil <= dto.EffectiveFrom))
            throw new InvalidOperationException("FISCAL_PROFILE_EFFECTIVE_DATE_INVALID");
        if (latest is not null && dto.EffectiveFrom == latest.EffectiveFrom
            && (await db.Sales.AnyAsync(s => s.CompanyId == context.CompanyId && s.FiscalProfileId == latest.Id)
                || await db.CreditNotes.AnyAsync(s => s.CompanyId == context.CompanyId && s.FiscalProfileId == latest.Id)))
            throw new InvalidOperationException("FISCAL_PROFILE_ALREADY_USED_CHANGE_NEXT_DATE");
        if (dto.Regime is null || dto.TaxpayerKind is null || !dto.Confirmed
            || !ValidReference(dto.Reason) || !ValidReference(dto.EvidenceReference))
            throw new InvalidOperationException("FISCAL_PROFILE_CONFIRMATION_REQUIRED");
        var profile = new CompanyFiscalProfile
        {
            CompanyId = context.CompanyId, Revision = checked(dto.ExpectedRevision + 1),
            Regime = dto.Regime.Value, TaxpayerKind = dto.TaxpayerKind.Value,
            IsAccountingRequired = dto.IsAccountingRequired,
            SpecialTaxpayerResolution = NormalizeSpecialResolution(dto.SpecialTaxpayerResolution),
            WithholdingAgentResolution = NormalizeAgentResolution(dto.WithholdingAgentResolution),
            EffectiveFrom = dto.EffectiveFrom, EffectiveUntil = dto.EffectiveUntil,
            EvidenceReference = dto.EvidenceReference.Trim(), Reason = dto.Reason.Trim(),
            DeclaredByUserId = context.UserId, DeclaredAt = now, Confirmed = true,
            RimpeEligibilityConfirmed = dto.RimpeEligibilityConfirmed
        };
        FiscalTaxResolver.ValidateProfile(profile);
        db.CompanyFiscalProfiles.Add(profile);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Map(profile, null);
    }

    public async Task<CompanyFiscalProfile> ResolveAsync(int companyId, DateOnly fiscalDate)
    {
        var profile = await db.CompanyFiscalProfiles.AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.EffectiveFrom <= fiscalDate)
            .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Revision).FirstOrDefaultAsync();
        if (profile is null || (profile.EffectiveUntil is { } until && fiscalDate >= until))
            throw new InvalidOperationException("FISCAL_PROFILE_REQUIRED");
        FiscalTaxResolver.ValidateProfile(profile);
        return profile;
    }

    public async Task<FiscalIssuerSnapshot> SnapshotAsync(OperationalContext context, DateOnly fiscalDate)
    {
        var profile = await ResolveAsync(context.CompanyId, fiscalDate);
        var establishment = await db.Establishments.AsNoTracking().Include(e => e.Company)
            .SingleAsync(e => e.CompanyId == context.CompanyId && e.Id == context.EstablishmentId);
        var company = establishment.Company;
        return new FiscalIssuerSnapshot(profile.Id, profile.Revision, profile.Regime, profile.TaxpayerKind,
            company.Name, company.Ruc, company.TradeName, company.MatrixAddress, establishment.Address,
            profile.IsAccountingRequired, profile.SpecialTaxpayerResolution, profile.WithholdingAgentResolution, fiscalDate);
    }

    public async Task<IReadOnlyDictionary<int, ProductFiscalClassification>> ResolveClassificationsAsync(
        int companyId, IReadOnlyCollection<int> productIds)
    {
        return await db.ProductFiscalClassifications.AsNoTracking()
            .Where(p => p.CompanyId == companyId && productIds.Contains(p.ProductId)
                && !db.ProductFiscalClassifications.Any(n => n.CompanyId == p.CompanyId
                    && n.ProductId == p.ProductId && n.Revision > p.Revision))
            .ToDictionaryAsync(p => p.ProductId);
    }

    public async Task<ProductFiscalStateDto> GetProductClassificationAsync(int productId)
    {
        var context = await accessor.GetRequiredContextAsync();
        var product = await GetProductAsync(context.CompanyId, productId);
        var classifications = await ResolveClassificationsAsync(context.CompanyId, new[] { productId });
        return new(product.FiscalRevision, product.Name, product.InternalCode, product.Barcode, product.VatCategory,
            classifications.TryGetValue(productId, out var c) ? Map(c, product) : null);
    }

    public async Task<ProductFiscalClassificationDto> DeclareProductClassificationAsync(int productId, DeclareProductFiscalClassificationDto dto)
    {
        var context = await accessor.GetRequiredContextAsync();
        var guard = new TenantAdministrationGuard(db);
        await using var tx = await guard.BeginChangeAsync(context.CompanyId);
        await guard.LockExclusiveOperationalWriteAsync(context);
        await RequirePermissionAsync(context);
        var product = await GetProductAsync(context.CompanyId, productId);
        if (dto.ExpectedProductRevision != product.FiscalRevision)
            throw new InvalidOperationException("PRODUCT_FISCAL_REVISION_CONFLICT");
        var latest = await db.ProductFiscalClassifications.Where(p => p.CompanyId == context.CompanyId && p.ProductId == productId)
            .OrderByDescending(p => p.Revision).FirstOrDefaultAsync();
        if ((latest?.Revision ?? 0) != dto.ExpectedRevision)
            throw new InvalidOperationException("PRODUCT_FISCAL_REVISION_CONFLICT");
        if (!dto.Confirmed || dto.Kind is null || dto.Activity is null || dto.TaxBasis is null
            || !Enum.IsDefined(dto.Kind.Value) || !Enum.IsDefined(dto.Activity.Value) || !Enum.IsDefined(dto.TaxBasis.Value)
            || !ValidReference(dto.LegalReference) || !ValidReference(dto.EvidenceReference))
            throw new InvalidOperationException("PRODUCT_FISCAL_CLASSIFICATION_INVALID");
        var classification = new ProductFiscalClassification
        {
            CompanyId = context.CompanyId, ProductId = productId, Revision = checked(dto.ExpectedRevision + 1),
            ProductFiscalRevision = product.FiscalRevision,
            DeclaredVatCategory = product.VatCategory, Kind = dto.Kind.Value, Activity = dto.Activity.Value,
            TaxBasis = dto.TaxBasis.Value, LegalReference = dto.LegalReference.Trim(),
            EvidenceReference = dto.EvidenceReference.Trim(), SriMaterialCode = dto.SriMaterialCode?.Trim(),
            NaturalAgriculturalFoodConfirmed = dto.NaturalAgriculturalFoodConfirmed,
            FoodProcessing = dto.FoodProcessing, ConstructionMaterialConfirmed = dto.ConstructionMaterialConfirmed,
            DeclaredByUserId = context.UserId, DeclaredAt = clock.UtcNow, Confirmed = true
        };
        FiscalTaxResolver.ValidateClassification(product, classification);
        ProductInventoryPolicy.ValidateClassification(product, classification);
        db.ProductFiscalClassifications.Add(classification);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Map(classification, product);
    }

    private async Task<Product> GetProductAsync(int companyId, int productId) => await db.Products.AsNoTracking()
        .SingleOrDefaultAsync(p => p.CompanyId == companyId && p.Id == productId)
        ?? throw new KeyNotFoundException("PRODUCT_NOT_FOUND");

    private Task RequirePermissionAsync(OperationalContext context) => ElectronicIssuingCoordinator.RequirePermissionsAsync(
        db, context.UserId, context.CompanyId, [AppPermissions.FiscalSettingsWrite]);
    private static bool ValidReference(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 500;
    private static string? NormalizeSpecialResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length is < 3 or > 13 || !value.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("FISCAL_RESOLUTION_INVALID");
        return value;
    }
    private static string? NormalizeAgentResolution(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (!value.All(char.IsAsciiDigit)) throw new InvalidOperationException("FISCAL_RESOLUTION_INVALID");
        value = value.TrimStart('0');
        if (value.Length is < 1 or > 8) throw new InvalidOperationException("FISCAL_RESOLUTION_INVALID");
        return value;
    }
    private static FiscalProfileDto Map(CompanyFiscalProfile p, DateOnly? successor)
        => new(p.Id, p.Revision, p.Regime, p.TaxpayerKind, p.IsAccountingRequired, p.SpecialTaxpayerResolution,
            p.WithholdingAgentResolution, p.EffectiveFrom,
            successor is not null && (p.EffectiveUntil is null || successor < p.EffectiveUntil) ? successor : p.EffectiveUntil,
            p.EvidenceReference, p.Reason, p.DeclaredByUserId, p.DeclaredAt, p.Confirmed, p.RimpeEligibilityConfirmed);
    private static ProductFiscalClassificationDto Map(ProductFiscalClassification c, Product p)
        => new(c.Revision, c.DeclaredVatCategory, c.Kind, c.Activity, c.TaxBasis, c.LegalReference, c.EvidenceReference,
            c.SriMaterialCode, c.DeclaredByUserId, c.DeclaredAt, c.Confirmed,
            c.DeclaredVatCategory == p.VatCategory && c.ProductFiscalRevision == p.FiscalRevision,
            c.NaturalAgriculturalFoodConfirmed, c.FoodProcessing, c.ConstructionMaterialConfirmed);
}
