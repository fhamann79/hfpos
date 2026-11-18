using System.Data;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class ProductBatchSelection
{
    public const int MaxProducts = 250;

    public static async Task<ProductFiscalBatchCandidates> CandidatesAsync(PosDbContext db, OperationalContext actor,
        string? search, bool? active, int? categoryId, string? fiscalStatus, ProductVatCategory? vatCategory,
        CancellationToken cancellationToken)
    {
        if (search?.Length > 150 || fiscalStatus is not (null or "pending" or "stale" or "current")
            || (vatCategory.HasValue && !Enum.IsDefined(vatCategory.Value)))
            throw new OperationalContextException("FISCAL_BATCH_FILTER_INVALID", 400);
        var products = db.Products.AsNoTracking().Where(p => p.CompanyId == actor.CompanyId);
        if (active.HasValue) products = products.Where(p => p.IsActive == active.Value);
        if (categoryId.HasValue) products = products.Where(p => p.CategoryId == categoryId.Value);
        if (vatCategory.HasValue) products = products.Where(p => p.VatCategory == vatCategory.Value);
        if (search?.Trim() is { Length: > 0 } text)
        {
            var term = text.ToLowerInvariant();
            products = products.Where(p => p.Name.ToLower().Contains(term)
                || (p.Barcode != null && p.Barcode.ToLower().Contains(term))
                || (p.InternalCode != null && p.InternalCode.ToLower().Contains(term)));
        }

        // "Current" describes the declaration, not the company's profile or ability to sell.
        var candidates = from p in products
            from c in db.ProductFiscalClassifications.AsNoTracking().Where(c => c.CompanyId == p.CompanyId && c.ProductId == p.Id)
                .OrderByDescending(c => c.Revision).Take(1).DefaultIfEmpty()
            select new { p.Id, p.Name, p.InternalCode, p.Barcode, p.CategoryId,
                p.IsActive, p.VatCategory, FiscalStatus = c == null ? "pending" :
                    c.Confirmed && c.ProductFiscalRevision == p.FiscalRevision && c.DeclaredVatCategory == p.VatCategory
                    ? "current" : "stale" };
        if (fiscalStatus is not null) candidates = candidates.Where(p => p.FiscalStatus == fiscalStatus);
        // Count and rows must describe the same snapshot even if another writer changes a match.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var total = await candidates.CountAsync(cancellationToken);
        var items = total is 0 or > MaxProducts ? [] : await candidates.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Take(MaxProducts)
            .Select(p => new ProductFiscalBatchCandidate(p.Id, p.Name, p.InternalCode, p.Barcode,
                p.CategoryId, p.IsActive, p.VatCategory, p.FiscalStatus)).ToArrayAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(total, MaxProducts, items);
    }

}
