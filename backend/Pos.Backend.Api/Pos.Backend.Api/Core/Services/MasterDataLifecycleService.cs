using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Core.Services;

public sealed class MasterDataLifecycleService(
    PosDbContext context,
    TenantAdministrationGuard administrationGuard) : IMasterDataLifecycleService
{
    public async Task SetCategoryActiveAsync(int companyId, int id, bool active)
    {
        await using var transaction = await administrationGuard.BeginChangeAsync(companyId);
        var category = await context.Categories.SingleOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId)
            ?? throw new KeyNotFoundException("CATEGORY_NOT_FOUND");
        EnsureStateChange(category.IsActive, active, "CATEGORY");

        if (!active && await context.Products.AnyAsync(p => p.CompanyId == companyId && p.CategoryId == id && p.IsActive))
            throw new MasterDataLifecycleException("CATEGORY_HAS_ACTIVE_PRODUCTS");

        category.IsActive = active;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetProductActiveAsync(int companyId, int id, bool active)
    {
        await using var transaction = await administrationGuard.BeginChangeAsync(companyId);
        var product = await context.Products.SingleOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId)
            ?? throw new KeyNotFoundException("PRODUCT_NOT_FOUND");
        EnsureStateChange(product.IsActive, active, "PRODUCT");

        if (active && !await context.Categories.AnyAsync(c => c.Id == product.CategoryId && c.CompanyId == companyId && c.IsActive))
            throw new MasterDataLifecycleException("CATEGORY_INACTIVE");

        product.IsActive = active;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task SetEstablishmentActiveAsync(int companyId, int id, bool active)
    {
        await using var transaction = await administrationGuard.BeginChangeAsync(companyId);
        var establishment = await context.Establishments.SingleOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId)
            ?? throw new KeyNotFoundException("ESTABLISHMENT_NOT_FOUND");
        EnsureStateChange(establishment.IsActive, active, "ESTABLISHMENT");

        if (!active)
        {
            if (await context.EmissionPoints.AnyAsync(ep => ep.EstablishmentId == id && ep.Establishment.CompanyId == companyId && ep.IsActive))
                throw new MasterDataLifecycleException("ESTABLISHMENT_HAS_ACTIVE_EMISSION_POINTS");
            if (await context.Users.AnyAsync(u => u.CompanyId == companyId && u.EstablishmentId == id && u.IsActive))
                throw new MasterDataLifecycleException("ESTABLISHMENT_HAS_ACTIVE_USERS");
            if (await context.CashSessions.AnyAsync(s => s.CompanyId == companyId && s.EstablishmentId == id && s.Status == CashSessionStatus.Open))
                throw new MasterDataLifecycleException("ESTABLISHMENT_HAS_OPEN_CASH_SESSIONS");
            if (await context.ProductStocks.AnyAsync(s => s.CompanyId == companyId && s.EstablishmentId == id && s.Quantity != 0m))
                throw new MasterDataLifecycleException("ESTABLISHMENT_HAS_STOCK");
            if (await HasPendingFiscalDocumentsAsync(companyId, id, null))
                throw new MasterDataLifecycleException("ESTABLISHMENT_HAS_PENDING_FISCAL_DOCUMENTS");
        }

        establishment.IsActive = active;
        await SaveStructureAsync(companyId, active);
        await transaction.CommitAsync();
    }

    public async Task SetEmissionPointActiveAsync(int companyId, int id, bool active)
    {
        await using var transaction = await administrationGuard.BeginChangeAsync(companyId);
        var emissionPoint = await context.EmissionPoints.Include(ep => ep.Establishment)
            .SingleOrDefaultAsync(ep => ep.Id == id && ep.Establishment.CompanyId == companyId)
            ?? throw new KeyNotFoundException("EMISSION_POINT_NOT_FOUND");
        EnsureStateChange(emissionPoint.IsActive, active, "EMISSION_POINT");

        if (active && !emissionPoint.Establishment.IsActive)
            throw new MasterDataLifecycleException("ESTABLISHMENT_INACTIVE");

        if (!active)
        {
            if (await context.Users.AnyAsync(u => u.CompanyId == companyId && u.EmissionPointId == id && u.IsActive))
                throw new MasterDataLifecycleException("EMISSION_POINT_HAS_ACTIVE_USERS");
            if (await context.CashSessions.AnyAsync(s => s.CompanyId == companyId && s.EmissionPointId == id && s.Status == CashSessionStatus.Open))
                throw new MasterDataLifecycleException("EMISSION_POINT_HAS_OPEN_CASH_SESSIONS");
            if (await HasPendingFiscalDocumentsAsync(companyId, emissionPoint.EstablishmentId, id))
                throw new MasterDataLifecycleException("EMISSION_POINT_HAS_PENDING_FISCAL_DOCUMENTS");
        }

        emissionPoint.IsActive = active;
        await SaveStructureAsync(companyId, active);
        await transaction.CommitAsync();
    }

    private async Task SaveStructureAsync(int companyId, bool active)
    {
        await context.SaveChangesAsync();
        if (!active && !await administrationGuard.HasActiveAdministratorAsync(companyId))
            throw new MasterDataLifecycleException("LAST_ACTIVE_ADMIN_REQUIRED");
    }

    private async Task<bool> HasPendingFiscalDocumentsAsync(int companyId, int establishmentId, int? emissionPointId)
    {
        // Rejected documents can still require correction/retry. Authorized and cancelled history is final.
        return await context.Sales.AnyAsync(s => s.CompanyId == companyId && s.EstablishmentId == establishmentId
                && (!emissionPointId.HasValue || s.EmissionPointId == emissionPointId.Value)
                && s.DocumentType == SaleDocumentType.Invoice && s.Status != SaleStatus.Voided
                && (s.DocumentStatus == SaleDocumentStatus.Draft
                    || s.DocumentStatus == SaleDocumentStatus.PendingAuthorization
                    || s.DocumentStatus == SaleDocumentStatus.Rejected))
            || await context.CreditNotes.AnyAsync(cn => cn.CompanyId == companyId && cn.EstablishmentId == establishmentId
                && (!emissionPointId.HasValue || cn.EmissionPointId == emissionPointId.Value)
                && (cn.DocumentStatus == SaleDocumentStatus.Draft
                    || cn.DocumentStatus == SaleDocumentStatus.PendingAuthorization
                    || cn.DocumentStatus == SaleDocumentStatus.Rejected));
    }

    private static void EnsureStateChange(bool current, bool requested, string resource)
    {
        if (current == requested)
            throw new MasterDataLifecycleException($"{resource}_ALREADY_{(requested ? "ACTIVE" : "INACTIVE")}");
    }
}
