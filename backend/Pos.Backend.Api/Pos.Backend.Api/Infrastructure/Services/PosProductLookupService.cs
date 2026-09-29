using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PosProductLookupService : IPosProductLookupService
{
    private const int DefaultTake = 30;
    private const int MaxTake = 100;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public PosProductLookupService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<IReadOnlyList<PosProductLookupDto>> SearchAsync(string? search, int take)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var limit = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        var term = search?.Trim().ToLowerInvariant();

        IQueryable<Product> query = _context.Products
            .AsNoTracking()
            .Where(product =>
                product.CompanyId == operationalContext.CompanyId
                && product.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(product =>
                product.Name.ToLower().Contains(term)
                || (product.Barcode != null && product.Barcode.ToLower().Contains(term))
                || (product.InternalCode != null && product.InternalCode.ToLower().Contains(term)));

            query = query
                .OrderByDescending(product => product.Barcode != null && product.Barcode.ToLower() == term)
                .ThenByDescending(product => product.InternalCode != null && product.InternalCode.ToLower() == term)
                .ThenByDescending(product => product.Name.ToLower().StartsWith(term))
                .ThenBy(product => product.Name)
                .ThenBy(product => product.Id);
        }
        else
        {
            query = query
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id);
        }

        return await query
            .Take(limit)
            .Select(product => new PosProductLookupDto
            {
                Id = product.Id,
                Name = product.Name,
                Barcode = product.Barcode,
                InternalCode = product.InternalCode,
                Price = product.Price,
                VatCategory = product.VatCategory,
                Stock = _context.ProductStocks
                    .Where(stock =>
                        stock.ProductId == product.Id
                        && stock.CompanyId == operationalContext.CompanyId
                        && stock.EstablishmentId == operationalContext.EstablishmentId)
                    .Select(stock => (decimal?)stock.Quantity)
                    .FirstOrDefault() ?? 0m,
                IsActive = product.IsActive
            })
            .ToListAsync();
    }
}
