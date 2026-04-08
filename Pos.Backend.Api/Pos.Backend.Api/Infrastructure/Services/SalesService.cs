using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SalesService : ISalesService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IInventoryService _inventoryService;

    public SalesService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IInventoryService inventoryService)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _inventoryService = inventoryService;
    }

    public async Task<IReadOnlyList<SaleListItemDto>> GetSalesAsync(DateTime? from, DateTime? to, SaleStatus? status, string? search, int? userId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.Sales
            .AsNoTracking()
            .Where(s => s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(s => s.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
            query = query.Where(s => s.CreatedAt <= toUtc);
        }

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(s => s.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                (s.Notes != null && s.Notes.ToLower().Contains(term))
                || (s.Number != null && s.Number.ToLower().Contains(term))
                || s.Id.ToString().Contains(term));
        }

        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new SaleListItemDto
            {
                Id = s.Id,
                Status = s.Status,
                Total = s.Total,
                ItemsCount = s.Items.Count,
                CreatedAt = s.CreatedAt,
                UserId = s.UserId,
                Username = s.User.Username,
                Notes = s.Notes
            })
            .ToListAsync();
    }

    public async Task<SaleDto?> GetByIdAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        return await _context.Sales
            .AsNoTracking()
            .Where(s => s.Id == id
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId)
            .Select(s => new SaleDto
            {
                Id = s.Id,
                Status = s.Status,
                Subtotal = s.Subtotal,
                Total = s.Total,
                Notes = s.Notes,
                CompanyId = s.CompanyId,
                EstablishmentId = s.EstablishmentId,
                EmissionPointId = s.EmissionPointId,
                UserId = s.UserId,
                CreatedAt = s.CreatedAt,
                Items = s.Items
                    .OrderBy(i => i.Id)
                    .Select(i => new SaleItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product.Name,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice,
                        LineSubtotal = i.LineSubtotal
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<SaleDto> CreateAsync(SaleCreateDto dto)
    {
        if (dto.Items is null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("SALE_ITEMS_REQUIRED");
        }

        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var itemProductIds = dto.Items
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => itemProductIds.Contains(p.Id) && p.CompanyId == operationalContext.CompanyId)
            .ToDictionaryAsync(p => p.Id);

        foreach (var requestedProductId in itemProductIds)
        {
            if (!products.ContainsKey(requestedProductId))
            {
                throw new KeyNotFoundException("PRODUCT_NOT_FOUND");
            }
        }

        foreach (var product in products.Values)
        {
            if (!product.IsActive)
            {
                throw new InvalidOperationException("PRODUCT_INACTIVE");
            }
        }

        var sale = new Sale
        {
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = operationalContext.EstablishmentId,
            EmissionPointId = operationalContext.EmissionPointId,
            UserId = operationalContext.UserId,
            Status = SaleStatus.Completed,
            Notes = dto.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var itemDto in dto.Items.OrderBy(i => i.ProductId))
        {
            if (itemDto.Quantity <= 0m)
            {
                throw new InvalidOperationException("INVALID_QUANTITY");
            }

            if (itemDto.UnitPrice < 0m)
            {
                throw new InvalidOperationException("INVALID_UNIT_PRICE");
            }

            var lineSubtotal = itemDto.Quantity * itemDto.UnitPrice;

            sale.Items.Add(new SaleItem
            {
                ProductId = itemDto.ProductId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                LineSubtotal = lineSubtotal
            });
        }

        sale.Subtotal = sale.Items.Sum(i => i.LineSubtotal);
        sale.Total = sale.Subtotal;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Sales.Add(sale);
        await _context.SaveChangesAsync();

        foreach (var item in sale.Items.OrderBy(i => i.ProductId))
        {
            await _inventoryService.RegisterSaleAsync(new InventoryExitDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Reference = $"SALE-{sale.Id}",
                Notes = sale.Notes
            });
        }

        await transaction.CommitAsync();

        var created = await GetByIdAsync(sale.Id);
        return created ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    public async Task<SaleDto> VoidAsync(int id, VoidSaleDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var sale = await _context.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        if (sale is null)
        {
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        }

        if (sale.Status == SaleStatus.Voided)
        {
            throw new InvalidOperationException("SALE_ALREADY_VOIDED");
        }

        if (sale.Status != SaleStatus.Completed)
        {
            throw new InvalidOperationException("SALE_NOT_VOIDABLE");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var voidNotes = string.IsNullOrWhiteSpace(dto.Reason)
            ? "Void sale"
            : dto.Reason.Trim();

        foreach (var item in sale.Items.OrderBy(i => i.ProductId))
        {
            await _inventoryService.RegisterVoidAsync(new InventoryEntryDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Reference = $"VOID-SALE-{sale.Id}",
                Notes = voidNotes
            });
        }

        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = DateTime.UtcNow;
        sale.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.Reason))
        {
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes)
                ? $"VOID: {voidNotes}"
                : $"{sale.Notes} | VOID: {voidNotes}";
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var response = await GetByIdAsync(sale.Id);
        return response ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }
}
