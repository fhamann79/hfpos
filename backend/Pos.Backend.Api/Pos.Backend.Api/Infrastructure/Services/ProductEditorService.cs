using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ProductEditorService(PosDbContext db, IOperationalContextAccessor accessor,
    TenantAdministrationGuard guard, IProductCostService costs, IFiscalProfileService profiles,
    IBusinessClockService clock, ISriFiscalClock fiscalClock) : IProductEditorService
{
    public async Task<ProductEditorStateDto> GetAsync(int id)
    {
        var actor = await accessor.GetRequiredContextAsync();
        // A bounded, coherent editor snapshot; no product/classification read can straddle an admin write.
        await using var tx = await guard.BeginChangeAsync(actor.CompanyId);
        await guard.LockExclusiveOperationalWriteAsync(actor);
        await RequireAsync(actor, AppPermissions.CatalogProductsRead);
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.CompanyId == actor.CompanyId && p.Id == id)
            ?? throw new OperationalContextException("PRODUCT_NOT_FOUND", 404);
        var result = await StateAsync(actor, product, await LatestAsync(product));
        await tx.CommitAsync();
        return result;
    }

    public async Task<ProductEditorStateDto> SaveAsync(int? id, ProductEditorSaveDto dto)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await using var tx = await guard.BeginChangeAsync(actor.CompanyId);
        try
        {
            await guard.LockExclusiveOperationalWriteAsync(actor);
            await RequireAsync(actor, AppPermissions.CatalogProductsWrite);
            if (dto.Classification is not null) await RequireAsync(actor, AppPermissions.FiscalSettingsWrite);
            Validate(dto);
            var product = id.HasValue
                ? await db.Products.SingleOrDefaultAsync(p => p.CompanyId == actor.CompanyId && p.Id == id.Value)
                    ?? throw new OperationalContextException("PRODUCT_NOT_FOUND", 404)
                : new Product { CompanyId = actor.CompanyId, IsActive = true, CreatedAt = clock.UtcNow };
            if (id.HasValue) await db.Entry(product).ReloadAsync();
            var previous = id.HasValue ? await LatestAsync(product) : null;
            if (id.HasValue && dto.ExpectedStateToken != Token(product, previous))
                throw new OperationalContextException("PRODUCT_EDITOR_STATE_CONFLICT", 409);
            if (dto.Classification is { } declaration
                && (declaration.ExpectedRevision != (previous?.Revision ?? 0)
                    || declaration.ExpectedProductRevision != product.FiscalRevision))
                throw new OperationalContextException("PRODUCT_FISCAL_REVISION_CONFLICT", 409);

            var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.CompanyId == actor.CompanyId && c.Id == dto.CategoryId)
                ?? throw new OperationalContextException("CATEGORY_NOT_FOUND", 400);
            if (!category.IsActive && (!id.HasValue || product.IsActive || product.CategoryId != dto.CategoryId))
                throw new OperationalContextException("CATEGORY_INACTIVE", 409);
            var barcode = Normalize(dto.Barcode); var internalCode = Normalize(dto.InternalCode);
            if (barcode is not null && await db.Products.AnyAsync(p => p.CompanyId == actor.CompanyId && p.Barcode == barcode && p.Id != (id ?? 0)))
                throw new OperationalContextException("PRODUCT_BARCODE_ALREADY_EXISTS", 409);
            if (internalCode is not null && await db.Products.AnyAsync(p => p.CompanyId == actor.CompanyId && p.InternalCode == internalCode && p.Id != (id ?? 0)))
                throw new OperationalContextException("PRODUCT_INTERNAL_CODE_ALREADY_EXISTS", 409);
            var vat = dto.VatCategory ?? (id.HasValue ? product.VatCategory : ProductVatCategory.Vat15);
            if (id.HasValue && (product.Name != dto.Name.Trim() || product.Barcode != barcode
                || product.InternalCode != internalCode || product.VatCategory != vat))
                product.FiscalRevision = checked(product.FiscalRevision + 1);
            product.Name = dto.Name.Trim(); product.CategoryId = dto.CategoryId;
            product.Barcode = barcode; product.InternalCode = internalCode; product.Price = dto.Price;
            product.MinimumStock = dto.MinimumStock; product.VatCategory = vat;
            if (!id.HasValue)
            {
                product.Cost = dto.Cost;
                db.Products.Add(product);
                // The generated ID is needed by the existing cost/classification audit records.
                await db.SaveChangesAsync();
                costs.InitializeManualCost(product, actor.UserId, clock.UtcNow);
            }
            else if (dto.SetManualCost || dto.Cost != product.Cost)
                costs.ApplyManualCost(product, dto.Cost, actor.UserId, clock.UtcNow);

            var wasTracking = product.TracksInventory;
            product.TracksInventory = dto.TracksInventory;
            var next = dto.Classification is null ? previous
                : ProductFiscalDeclaration.Create(product, dto.Classification, actor.UserId, clock.UtcNow);
            product.TracksInventory = wasTracking;
            if (!id.HasValue)
            {
                product.TracksInventory = dto.TracksInventory;
                ProductInventoryPolicy.ValidateClassification(product, next);
            }
            else await ProductInventoryModeService.ApplyAsync(db, product, dto.TracksInventory, next?.Revision, next);
            if (dto.Classification is not null) db.ProductFiscalClassifications.Add(next!);
            await db.SaveChangesAsync();
            await db.Entry(product).ReloadAsync();
            var result = await StateAsync(actor, product, next);
            await tx.CommitAsync();
            return result;
        }
        catch (InvalidOperationException ex) when (FiscalProfileErrors.IsValidation(ex.Message)
            || ex.Message is "PRODUCT_CLASSIFICATION_REVISION_CONFLICT" or "PRODUCT_INVENTORY_MODE_HAS_STOCK")
        {
            db.ChangeTracker.Clear();
            throw new OperationalContextException(ex.Message, 409);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<ProductFiscalClassification?> LatestAsync(Product p) => db.ProductFiscalClassifications.AsNoTracking()
        .Where(c => c.CompanyId == p.CompanyId && c.ProductId == p.Id).OrderByDescending(c => c.Revision).FirstOrDefaultAsync();

    private async Task RequireAsync(OperationalContext actor, string permission)
    {
        if (!await HasAsync(actor, permission)) throw new OperationalContextException("FISCAL_PERMISSION_REQUIRED", 403);
    }

    private Task<bool> HasAsync(OperationalContext actor, string permission) => db.Users.AsNoTracking()
        .AnyAsync(u => u.Id == actor.UserId && u.CompanyId == actor.CompanyId && u.IsActive
            && u.Role.CompanyId == actor.CompanyId && u.Role.IsActive
            && u.Role.RolePermissions.Any(r => r.Permission.IsActive && r.Permission.Code == permission));

    private async Task<ProductEditorStateDto> StateAsync(OperationalContext actor, Product p, ProductFiscalClassification? c)
    {
        var pending = new List<string>();
        if (!p.IsActive) pending.Add("PRODUCT_INACTIVE");
        if (!await db.Categories.AnyAsync(x => x.CompanyId == p.CompanyId && x.Id == p.CategoryId && x.IsActive))
            pending.Add("CATEGORY_INACTIVE");
        var canDisable = false;
        try
        {
            var original = p.TracksInventory;
            try { p.TracksInventory = false; ProductInventoryPolicy.ValidateClassification(p, c); canDisable = true; }
            finally { p.TracksInventory = original; }
        }
        catch (InvalidOperationException ex) when (FiscalProfileErrors.IsValidation(ex.Message)) { }
        try
        {
            ProductInventoryPolicy.ValidateClassification(p, c);
            var date = fiscalClock.GetEcuadorFiscalDate(clock.UtcNow);
            var profile = await profiles.ResolveAsync(actor.CompanyId, date);
            FiscalTaxResolver.Resolve(profile, p, c, date);
        }
        catch (InvalidOperationException ex) when (FiscalProfileErrors.IsValidation(ex.Message)) { pending.Add(ex.Message); }
        if (p.TracksInventory && !await db.ProductStocks.AnyAsync(s => s.CompanyId == p.CompanyId
            && s.EstablishmentId == actor.EstablishmentId && s.ProductId == p.Id && s.Quantity > 0))
            pending.Add("INSUFFICIENT_STOCK");
        var visibleClassification = await HasAsync(actor, AppPermissions.FiscalSettingsRead);
        return new(Map(p), Token(p, c), p.FiscalRevision, c?.Revision ?? 0,
            visibleClassification && c is not null ? ProductFiscalDeclaration.Map(c, p) : null, canDisable, pending);
    }

    private static string Token(Product p, ProductFiscalClassification? c) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { Product = Map(p), p.CompanyId, p.FiscalRevision,
            p.LastCostRevision, p.CurrentCostEventId, ClassificationRevision = c?.Revision ?? 0 })));

    private static ProductDto Map(Product p) => new() { Id = p.Id, CategoryId = p.CategoryId, Name = p.Name,
        Barcode = p.Barcode, InternalCode = p.InternalCode, Price = p.Price, Cost = p.Cost,
        MinimumStock = p.MinimumStock, VatCategory = p.VatCategory, TracksInventory = p.TracksInventory, IsActive = p.IsActive };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Validate(ProductEditorSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new OperationalContextException("NAME_REQUIRED", 400);
        if (dto.Name.Trim().Length > 150 || Normalize(dto.Barcode)?.Length > 100 || Normalize(dto.InternalCode)?.Length > 100)
            throw new OperationalContextException("PRODUCT_IDENTIFICATION_INVALID", 400);
        if (dto.Cost < 0) throw new OperationalContextException("PRODUCT_COST_INVALID", 400);
        if (dto.MinimumStock < 0) throw new OperationalContextException("PRODUCT_MINIMUM_STOCK_INVALID", 400);
        if (dto.Price < 0) throw new OperationalContextException("INVALID_PRICE", 400);
        if (dto.VatCategory.HasValue && !Enum.IsDefined(dto.VatCategory.Value))
            throw new OperationalContextException("INVALID_PRODUCT_VAT_CATEGORY", 400);
    }
}
