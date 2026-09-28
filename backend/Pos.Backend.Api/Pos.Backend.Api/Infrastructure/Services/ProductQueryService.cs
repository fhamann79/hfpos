using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ProductQueryService : IProductQueryService
{
    private const int MaxLookupTake = 200;
    private const int MaxPageSize = 200;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public ProductQueryService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<IReadOnlyList<ProductDto>> GetLegacyCatalogAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        return await _context.Products
            .AsNoTracking()
            .Where(product => product.CompanyId == operationalContext.CompanyId)
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(product => ToDto(product))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ProductDto>> GetLookupAsync(
        string? search,
        int take,
        int? categoryId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var limit = Math.Clamp(take, 1, MaxLookupTake);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, "active", categoryId);

        return await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Take(limit)
            .Select(product => ToDto(product))
            .ToListAsync();
    }

    public async Task<PagedResultDto<ProductDto>> GetPageAsync(
        string? search,
        string? status,
        int? categoryId,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, status, categoryId);
        var totalItems = await query.CountAsync();
        var ordered = ApplyOrdering(query, sortBy, sortDir);

        var items = await ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(product => ToDto(product))
            .ToListAsync();

        return new PagedResultDto<ProductDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize)
        };
    }

    private IQueryable<Product> BuildFilteredQuery(
        int companyId,
        string? search,
        string? status,
        int? categoryId)
    {
        var query = _context.Products
            .AsNoTracking()
            .Where(product => product.CompanyId == companyId);

        if (categoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        var normalizedStatus = status?.Trim().ToLowerInvariant();
        query = normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(product => product.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(product => !product.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(term)
                || (product.Barcode != null && product.Barcode.ToLower().Contains(term))
                || (product.InternalCode != null && product.InternalCode.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<Product> ApplyOrdering(
        IQueryable<Product> query,
        string? sortBy,
        string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(product => product.Name).ThenByDescending(product => product.Id)
                : query.OrderBy(product => product.Name).ThenBy(product => product.Id),
            "price" => descending
                ? query.OrderByDescending(product => product.Price).ThenByDescending(product => product.Id)
                : query.OrderBy(product => product.Price).ThenBy(product => product.Id),
            "cost" => descending
                ? query.OrderByDescending(product => product.Cost).ThenByDescending(product => product.Id)
                : query.OrderBy(product => product.Cost).ThenBy(product => product.Id),
            "isactive" => descending
                ? query.OrderByDescending(product => product.IsActive).ThenBy(product => product.Name).ThenBy(product => product.Id)
                : query.OrderBy(product => product.IsActive).ThenBy(product => product.Name).ThenBy(product => product.Id),
            _ => query.OrderByDescending(product => product.IsActive).ThenBy(product => product.Name).ThenBy(product => product.Id)
        };
    }

    private static ProductDto ToDto(Product product)
        => new()
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            Name = product.Name,
            Barcode = product.Barcode,
            InternalCode = product.InternalCode,
            Price = product.Price,
            Cost = product.Cost,
            MinimumStock = product.MinimumStock,
            VatCategory = product.VatCategory,
            IsActive = product.IsActive
        };
}
