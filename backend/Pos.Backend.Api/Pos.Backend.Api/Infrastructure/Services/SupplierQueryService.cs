using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class SupplierQueryService : ISupplierQueryService
{
    private const int DefaultLookupTake = 50;
    private const int MaxLookupTake = 200;
    private const int MaxPageSize = 200;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public SupplierQueryService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<IReadOnlyList<SupplierDto>> GetListAsync(
        string? search,
        int? take,
        bool activeOnly)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var query = BuildFilteredQuery(
            operationalContext.CompanyId,
            search,
            activeOnly ? "active" : "all");

        var ordered = query
            .OrderByDescending(supplier => supplier.IsActive)
            .ThenBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id);

        var projected = ordered.Select(supplier => ToDto(supplier));
        if (take.HasValue)
        {
            projected = projected.Take(Math.Clamp(take.Value, 1, MaxLookupTake));
        }

        return await projected.ToListAsync();
    }

    public async Task<IReadOnlyList<SupplierDto>> GetLookupAsync(string? search, int take)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var limit = Math.Clamp(take, 1, MaxLookupTake);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, "active");

        return await query
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Take(limit)
            .Select(supplier => ToDto(supplier))
            .ToListAsync();
    }

    public async Task<PagedResultDto<SupplierDto>> GetPageAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, status);
        var totalItems = await query.CountAsync();
        var ordered = ApplyOrdering(query, sortBy, sortDir);
        var offset = ((long)normalizedPage - 1L) * normalizedPageSize;

        IReadOnlyList<SupplierDto> items;
        if (offset > int.MaxValue)
        {
            items = Array.Empty<SupplierDto>();
        }
        else
        {
            items = await ordered
                .Skip((int)offset)
                .Take(normalizedPageSize)
                .Select(supplier => ToDto(supplier))
                .ToListAsync();
        }

        return new PagedResultDto<SupplierDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize)
        };
    }

    private IQueryable<Supplier> BuildFilteredQuery(int companyId, string? search, string? status)
    {
        var query = _context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.CompanyId == companyId);

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        query = normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(supplier => supplier.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(supplier => !supplier.IsActive),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(supplier =>
                supplier.Name.ToLower().Contains(term)
                || (supplier.Identification != null && supplier.Identification.ToLower().Contains(term))
                || (supplier.Email != null && supplier.Email.ToLower().Contains(term))
                || (supplier.Phone != null && supplier.Phone.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<Supplier> ApplyOrdering(
        IQueryable<Supplier> query,
        string? sortBy,
        string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(supplier => supplier.Name).ThenByDescending(supplier => supplier.Id)
                : query.OrderBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id),
            "identification" => descending
                ? query.OrderByDescending(supplier => supplier.Identification).ThenByDescending(supplier => supplier.Id)
                : query.OrderBy(supplier => supplier.Identification).ThenBy(supplier => supplier.Id),
            "isactive" => descending
                ? query.OrderByDescending(supplier => supplier.IsActive).ThenBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id)
                : query.OrderBy(supplier => supplier.IsActive).ThenBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id),
            "updatedat" => descending
                ? query.OrderByDescending(supplier => supplier.UpdatedAt ?? supplier.CreatedAt).ThenByDescending(supplier => supplier.Id)
                : query.OrderBy(supplier => supplier.UpdatedAt ?? supplier.CreatedAt).ThenBy(supplier => supplier.Id),
            _ => query.OrderByDescending(supplier => supplier.IsActive).ThenBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id)
        };
    }

    private static SupplierDto ToDto(Supplier supplier)
        => new()
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Identification = supplier.Identification,
            Email = supplier.Email,
            Phone = supplier.Phone,
            Address = supplier.Address,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt,
            UpdatedAt = supplier.UpdatedAt
        };
}
