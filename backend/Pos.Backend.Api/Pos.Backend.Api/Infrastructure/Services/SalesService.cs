using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SalesService : ISalesService
{
    private readonly PosDbContext _context;
    private readonly ILogger<SalesService> _logger;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IInventoryService _inventoryService;

    public SalesService(
        PosDbContext context,
        ILogger<SalesService> logger,
        IOperationalContextAccessor operationalContextAccessor,
        IInventoryService inventoryService)
    {
        _context = context;
        _logger = logger;
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
                CustomerId = s.CustomerId,
                CustomerName = s.Customer != null ? s.Customer.Name : null,
                PaymentMethod = s.PaymentMethod,
                DocumentType = s.DocumentType,
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
        OperationalContext? operationalContext = null;

        try
        {
            if (dto.Items is null || dto.Items.Count == 0)
            {
                throw new InvalidOperationException("SALE_ITEMS_REQUIRED");
            }

            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            var paymentMethod = dto.PaymentMethod ?? SalePaymentMethod.Cash;
            var documentType = dto.DocumentType ?? SaleDocumentType.Ticket;

            if (!Enum.IsDefined(paymentMethod))
            {
                throw new InvalidOperationException("INVALID_SALE_PAYMENT_METHOD");
            }

            if (!Enum.IsDefined(documentType))
            {
                throw new InvalidOperationException("INVALID_SALE_DOCUMENT_TYPE");
            }

            Customer? customer = null;

            if (dto.CustomerId.HasValue)
            {
                customer = await _context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.Id == dto.CustomerId.Value
                        && c.CompanyId == operationalContext.CompanyId
                        && c.IsActive);

                if (customer is null)
                {
                    throw new KeyNotFoundException("CUSTOMER_NOT_FOUND");
                }
            }

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
                CustomerId = customer?.Id,
                Status = SaleStatus.Completed,
                PaymentMethod = paymentMethod,
                DocumentType = documentType,
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
                await _inventoryService.RegisterSaleAsync(
                    item.ProductId,
                    item.Quantity,
                    sale.Id,
                    item.Id,
                    sale.Notes);
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Sale created successfully. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount} Total {Total}",
                sale.Id,
                operationalContext.UserId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                sale.Items.Count,
                sale.Total);

            var created = await GetByIdAsync(sale.Id);
            return created ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Sale creation failed. ErrorCode {ErrorCode} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount}",
                ex.Message,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId,
                dto.Items?.Count ?? 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error creating sale. UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ItemsCount {ItemsCount}",
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId,
                dto.Items?.Count ?? 0);
            throw;
        }
    }

    public async Task<SaleDto> VoidAsync(int id, VoidSaleDto dto)
    {
        OperationalContext? operationalContext = null;

        try
        {
            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Sales""
                    WHERE ""Id"" = {id}
                      AND ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                      AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                    FOR UPDATE")
                .SingleOrDefaultAsync();

            if (sale is null)
            {
                throw new KeyNotFoundException("SALE_NOT_FOUND");
            }

            await _context.Entry(sale)
                .Collection(s => s.Items)
                .LoadAsync();

            if (sale.Status == SaleStatus.Voided)
            {
                throw new InvalidOperationException("SALE_ALREADY_VOIDED");
            }

            if (sale.Status != SaleStatus.Completed)
            {
                throw new InvalidOperationException("SALE_NOT_VOIDABLE");
            }

            var voidNotes = string.IsNullOrWhiteSpace(dto.Reason)
                ? "Void sale"
                : dto.Reason.Trim();

            foreach (var item in sale.Items.OrderBy(i => i.ProductId))
            {
                await _inventoryService.RegisterVoidAsync(
                    item.ProductId,
                    item.Quantity,
                    sale.Id,
                    item.Id,
                    voidNotes);
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

            _logger.LogInformation(
                "Sale voided successfully. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                sale.Id,
                operationalContext.UserId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);

            var response = await GetByIdAsync(sale.Id);
            return response ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Sale void failed. SaleId {SaleId} ErrorCode {ErrorCode} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                id,
                ex.Message,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error voiding sale. SaleId {SaleId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                id,
                operationalContext?.UserId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw;
        }
    }
}
