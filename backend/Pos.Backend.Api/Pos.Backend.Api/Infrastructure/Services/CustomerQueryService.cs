using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class CustomerQueryService : ICustomerQueryService
{
    private const int DefaultTake = 30;
    private const int MaxTake = 200;
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 200;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public CustomerQueryService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<IReadOnlyList<CustomerDto>> GetLookupAsync(
        string? search,
        bool? includeInactive,
        string? status,
        int? take)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, includeInactive, status);
        var limit = Math.Clamp(take ?? DefaultTake, 1, MaxTake);

        return await query
            .OrderByDescending(customer => customer.IsActive)
            .ThenBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Take(limit)
            .Select(customer => ToDto(customer))
            .ToListAsync();
    }

    public async Task<PagedResultDto<CustomerDto>> GetPageAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDir)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var query = BuildFilteredQuery(operationalContext.CompanyId, search, includeInactive: null, status);
        var totalItems = await query.CountAsync();
        var ordered = ApplyOrdering(query, sortBy, sortDir);

        var items = await ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(customer => ToDto(customer))
            .ToListAsync();

        return new PagedResultDto<CustomerDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)normalizedPageSize)
        };
    }

    private IQueryable<Customer> BuildFilteredQuery(
        int companyId,
        string? search,
        bool? includeInactive,
        string? status)
    {
        var query = _context.Customers
            .AsNoTracking()
            .Where(customer => customer.CompanyId == companyId);

        query = ApplyStatusFilter(query, includeInactive, status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(customer =>
                customer.Name.ToLower().Contains(term)
                || (customer.Identification != null && customer.Identification.ToLower().Contains(term))
                || (customer.Email != null && customer.Email.ToLower().Contains(term))
                || (customer.Phone != null && customer.Phone.ToLower().Contains(term))
                || (customer.Address != null && customer.Address.ToLower().Contains(term)));
        }

        return query;
    }

    private static IOrderedQueryable<Customer> ApplyOrdering(
        IQueryable<Customer> query,
        string? sortBy,
        string? sortDir)
    {
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSort = sortBy?.Trim().ToLowerInvariant();

        return normalizedSort switch
        {
            "name" => descending
                ? query.OrderByDescending(customer => customer.Name).ThenByDescending(customer => customer.Id)
                : query.OrderBy(customer => customer.Name).ThenBy(customer => customer.Id),
            "isactive" => descending
                ? query.OrderByDescending(customer => customer.IsActive).ThenBy(customer => customer.Name).ThenBy(customer => customer.Id)
                : query.OrderBy(customer => customer.IsActive).ThenBy(customer => customer.Name).ThenBy(customer => customer.Id),
            "updatedat" => descending
                ? query.OrderByDescending(customer => customer.UpdatedAt ?? customer.CreatedAt).ThenByDescending(customer => customer.Id)
                : query.OrderBy(customer => customer.UpdatedAt ?? customer.CreatedAt).ThenBy(customer => customer.Id),
            _ => query.OrderByDescending(customer => customer.IsActive).ThenBy(customer => customer.Name).ThenBy(customer => customer.Id)
        };
    }

    private static IQueryable<Customer> ApplyStatusFilter(
        IQueryable<Customer> query,
        bool? includeInactive,
        string? status)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();

        return normalizedStatus switch
        {
            "active" or "activo" or "activos" => query.Where(customer => customer.IsActive),
            "inactive" or "inactivo" or "inactivos" => query.Where(customer => !customer.IsActive),
            "all" or "todos" => query,
            _ => includeInactive == true ? query : query.Where(customer => customer.IsActive)
        };
    }

    private static CustomerDto ToDto(Customer customer)
        => new()
        {
            Id = customer.Id,
            Name = customer.Name,
            IdentificationType = customer.IdentificationType,
            Identification = customer.Identification,
            Phone = customer.Phone,
            Email = customer.Email,
            Address = customer.Address,
            Notes = customer.Notes,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt
        };
}
