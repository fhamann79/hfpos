using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PosQuickProductService(
    PosDbContext db, IOperationalContextAccessor contextAccessor) : IPosQuickProductService
{
    private const int Limit = 12;

    public async Task<PosFavoritesDto> GetFavoritesAsync(CancellationToken cancellationToken)
    {
        var context = await contextAccessor.GetRequiredContextAsync();
        return await db.PosFavoriteLists.AsNoTracking()
            .Where(list => list.CompanyId == context.CompanyId)
            .Select(list => new PosFavoritesDto(list.Revision, list.ProductIds))
            .SingleOrDefaultAsync(cancellationToken) ?? new PosFavoritesDto(0, []);
    }

    public async Task<PosFavoritesDto> SaveFavoritesAsync(SavePosFavoritesDto dto, CancellationToken cancellationToken)
    {
        var context = await contextAccessor.GetRequiredContextAsync();
        if (dto.Revision is not long revision || revision < 0 || revision >= long.MaxValue
            || dto.ProductIds is not { Length: <= Limit } ids
            || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("POS_FAVORITES_INVALID");

        var current = await GetFavoritesAsync(cancellationToken);
        if (current.Revision != revision)
            throw new InvalidOperationException("POS_FAVORITES_STALE");

        // Existing inactive entries remain removable; only newly added entries must be active.
        var added = ids.Except(current.ProductIds).ToArray();
        if (added.Length != 0 && await db.Products.CountAsync(product => product.CompanyId == context.CompanyId
                && product.IsActive && added.Contains(product.Id), cancellationToken) != added.Length)
            throw new InvalidOperationException("POS_FAVORITES_PRODUCT_INVALID");

        var next = revision + 1;
        var written = revision == 0
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PosFavoriteLists" ("CompanyId", "Revision", "ProductIds")
                VALUES ({context.CompanyId}, {next}, {ids})
                ON CONFLICT ("CompanyId") DO NOTHING
                """, cancellationToken)
            : await db.PosFavoriteLists
                .Where(list => list.CompanyId == context.CompanyId && list.Revision == revision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(list => list.Revision, next)
                    .SetProperty(list => list.ProductIds, ids), cancellationToken);
        if (written != 1)
            throw new InvalidOperationException("POS_FAVORITES_STALE");

        return new PosFavoritesDto(next, ids);
    }

    public async Task<int[]> GetTopSellersAsync(CancellationToken cancellationToken)
    {
        var context = await contextAccessor.GetRequiredContextAsync();
        var now = DateTime.UtcNow;
        var since = now.AddDays(-30);
        var sales = db.Sales.Where(sale => sale.CompanyId == context.CompanyId
            && sale.EstablishmentId == context.EstablishmentId && sale.EmissionPointId == context.EmissionPointId
            && sale.Status == SaleStatus.Completed && sale.VoidedAt == null
            && sale.CreatedAt >= since && sale.CreatedAt <= now);

        // Attribute returns to the original sale's cohort, not to the date the note was issued.
        var sold = from sale in sales
                   from item in sale.Items
                   select new { item.ProductId, Units = item.Quantity };
        var returned = from sale in sales
                       join note in CreditNoteReporting.AuthorizedNotesInContext(db, context)
                           on sale.Id equals note.OriginalSaleId
                       from item in note.Items
                       select new { item.ProductId, Units = -item.Quantity };

        return await sold.Concat(returned)
            .Where(line => db.Products.Any(product => product.Id == line.ProductId
                && product.CompanyId == context.CompanyId && product.IsActive))
            .GroupBy(line => line.ProductId)
            .Select(group => new { ProductId = group.Key, Units = group.Sum(line => line.Units) })
            .Where(product => product.Units > 0)
            .OrderByDescending(product => product.Units)
            .ThenBy(product => product.ProductId)
            .Take(Limit)
            .Select(product => product.ProductId)
            .ToArrayAsync(cancellationToken);
    }
}
