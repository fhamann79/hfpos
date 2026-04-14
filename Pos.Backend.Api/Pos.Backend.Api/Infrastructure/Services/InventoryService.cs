using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly PosDbContext _context;
    private readonly ILogger<InventoryService> _logger;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public InventoryService(
        PosDbContext context,
        ILogger<InventoryService> logger,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _logger = logger;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<IReadOnlyList<InventoryStockListItemDto>> GetStocksAsync(string? search, int? productId, bool onlyPositive)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.CompanyId == operationalContext.CompanyId)
            .Select(p => new
            {
                Product = p,
                CategoryName = p.Category.Name,
                Quantity = _context.ProductStocks
                    .Where(s => s.ProductId == p.Id
                        && s.CompanyId == operationalContext.CompanyId
                        && s.EstablishmentId == operationalContext.EstablishmentId)
                    .Select(s => (decimal?)s.Quantity)
                    .FirstOrDefault() ?? 0m
            });

        if (productId.HasValue)
        {
            query = query.Where(x => x.Product.Id == productId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Product.Name.ToLower().Contains(term) || x.CategoryName.ToLower().Contains(term));
        }

        if (onlyPositive)
        {
            query = query.Where(x => x.Quantity > 0m);
        }

        return await query
            .OrderBy(x => x.Product.Name)
            .Select(x => new InventoryStockListItemDto
            {
                ProductId = x.Product.Id,
                ProductName = x.Product.Name,
                CategoryId = x.Product.CategoryId,
                CategoryName = x.CategoryName,
                Quantity = x.Quantity,
                IsActive = x.Product.IsActive
            })
            .ToListAsync();
    }

    public async Task<InventoryStockDto?> GetProductStockAsync(int productId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await GetProductInCompanyAsync(productId, operationalContext.CompanyId);

        var quantity = await _context.ProductStocks
            .AsNoTracking()
            .Where(ps => ps.ProductId == product.Id
                && ps.CompanyId == operationalContext.CompanyId
                && ps.EstablishmentId == operationalContext.EstablishmentId)
            .Select(ps => (decimal?)ps.Quantity)
            .FirstOrDefaultAsync() ?? 0m;

        return new InventoryStockDto
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = quantity,
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = operationalContext.EstablishmentId
        };
    }

    public async Task<IReadOnlyList<InventoryMovementDto>> GetProductMovementsAsync(int productId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var product = await GetProductInCompanyAsync(productId, operationalContext.CompanyId);

        return await BuildMovementQuery(operationalContext.CompanyId, operationalContext.EstablishmentId)
            .AsNoTracking()
            .Where(m => m.ProductId == product.Id)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new InventoryMovementDto
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                Type = m.Type,
                SourceType = m.SourceType,
                SourceId = m.SourceId,
                SourceLineId = m.SourceLineId,
                Quantity = m.Quantity,
                StockBefore = m.StockBefore,
                StockAfter = m.StockAfter,
                Reference = m.Reference,
                Notes = m.Notes,
                UserId = m.UserId,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PagedResultDto<InventoryMovementDto>> GetMovementsAsync(InventoryMovementQueryDto query)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var movements = BuildMovementQuery(operationalContext.CompanyId, operationalContext.EstablishmentId)
            .AsNoTracking();

        if (query.ProductId.HasValue)
        {
            await GetProductInCompanyAsync(query.ProductId.Value, operationalContext.CompanyId);
            movements = movements.Where(m => m.ProductId == query.ProductId.Value);
        }

        if (query.Type.HasValue)
        {
            movements = movements.Where(m => m.Type == query.Type.Value);
        }

        if (query.SourceType.HasValue)
        {
            movements = movements.Where(m => m.SourceType == query.SourceType.Value);
        }

        if (query.SourceId.HasValue)
        {
            movements = movements.Where(m => m.SourceId == query.SourceId.Value);
        }

        if (query.From.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(query.From.Value, DateTimeKind.Utc);
            movements = movements.Where(m => m.CreatedAt >= fromUtc);
        }

        if (query.To.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(query.To.Value, DateTimeKind.Utc);
            movements = movements.Where(m => m.CreatedAt <= toUtc);
        }

        if (query.UserId.HasValue)
        {
            movements = movements.Where(m => m.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            movements = movements.Where(m =>
                m.Product.Name.ToLower().Contains(term)
                || (m.Reference != null && m.Reference.ToLower().Contains(term))
                || (m.Notes != null && m.Notes.ToLower().Contains(term)));
        }

        var totalItems = await movements.CountAsync();
        var items = await movements
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new InventoryMovementDto
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                Type = m.Type,
                SourceType = m.SourceType,
                SourceId = m.SourceId,
                SourceLineId = m.SourceLineId,
                Quantity = m.Quantity,
                StockBefore = m.StockBefore,
                StockAfter = m.StockAfter,
                Reference = m.Reference,
                Notes = m.Notes,
                UserId = m.UserId,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return new PagedResultDto<InventoryMovementDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<InventoryMovementDto?> GetMovementByIdAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        return await BuildMovementQuery(operationalContext.CompanyId, operationalContext.EstablishmentId)
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new InventoryMovementDto
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product.Name,
                Type = m.Type,
                SourceType = m.SourceType,
                SourceId = m.SourceId,
                SourceLineId = m.SourceLineId,
                Quantity = m.Quantity,
                StockBefore = m.StockBefore,
                StockAfter = m.StockAfter,
                Reference = m.Reference,
                Notes = m.Notes,
                UserId = m.UserId,
                CreatedAt = m.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    public Task<InventoryMovementDto> RegisterEntryAsync(InventoryEntryDto dto)
    {
        if (dto.Quantity <= 0m)
        {
            throw new InvalidOperationException("INVALID_QUANTITY");
        }

        return RegisterMovementAsync(
            dto.ProductId,
            InventoryMovementType.Entry,
            InventoryMovementSourceType.ManualEntry,
            dto.Quantity,
            dto.Reference,
            dto.Notes,
            null,
            null);
    }

    public Task<InventoryMovementDto> RegisterExitAsync(InventoryExitDto dto)
    {
        if (dto.Quantity <= 0m)
        {
            throw new InvalidOperationException("INVALID_QUANTITY");
        }

        return RegisterMovementAsync(
            dto.ProductId,
            InventoryMovementType.Exit,
            InventoryMovementSourceType.ManualExit,
            dto.Quantity,
            dto.Reference,
            dto.Notes,
            null,
            null);
    }

    public Task<InventoryMovementDto> RegisterAdjustmentAsync(InventoryAdjustDto dto)
    {
        if (dto.Quantity < 0m)
        {
            throw new InvalidOperationException("INVALID_QUANTITY");
        }

        return RegisterMovementAsync(
            dto.ProductId,
            InventoryMovementType.Adjustment,
            InventoryMovementSourceType.ManualAdjustment,
            dto.Quantity,
            dto.Reference,
            dto.Notes,
            null,
            null);
    }

    public Task<InventoryMovementDto> RegisterSaleAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes)
    {
        if (quantity <= 0m)
        {
            throw new InvalidOperationException("INVALID_QUANTITY");
        }

        return RegisterMovementAsync(
            productId,
            InventoryMovementType.Sale,
            InventoryMovementSourceType.Sale,
            quantity,
            $"SALE-{saleId}",
            notes,
            saleId,
            saleItemId);
    }

    public Task<InventoryMovementDto> RegisterVoidAsync(int productId, decimal quantity, int saleId, int saleItemId, string? notes)
    {
        if (quantity <= 0m)
        {
            throw new InvalidOperationException("INVALID_QUANTITY");
        }

        return RegisterMovementAsync(
            productId,
            InventoryMovementType.Void,
            InventoryMovementSourceType.SaleVoid,
            quantity,
            $"VOID-SALE-{saleId}",
            notes,
            saleId,
            saleItemId);
    }

    private async Task<InventoryMovementDto> RegisterMovementAsync(
        int productId,
        InventoryMovementType type,
        InventoryMovementSourceType sourceType,
        decimal quantity,
        string? reference,
        string? notes,
        int? sourceId,
        int? sourceLineId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var product = await GetValidProductAsync(productId, operationalContext.CompanyId);

        try
        {
            var hasAmbientTransaction = _context.Database.CurrentTransaction is not null;
            await using var transaction = hasAmbientTransaction
                ? null
                : await _context.Database.BeginTransactionAsync();

            var productStock = await GetLockedProductStockAsync(
                product.Id,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId);

            var canCreateStock = type == InventoryMovementType.Entry
                || type == InventoryMovementType.Adjustment
                || type == InventoryMovementType.Void;

            if (productStock is null && canCreateStock)
            {
                productStock = await CreateAndLockProductStockAsync(
                    product.Id,
                    operationalContext.CompanyId,
                    operationalContext.EstablishmentId);
            }

            var stockBefore = productStock?.Quantity ?? 0m;
            decimal stockAfter;

            switch (type)
            {
                case InventoryMovementType.Entry:
                    stockAfter = stockBefore + quantity;
                    break;
                case InventoryMovementType.Exit:
                    if (stockBefore < quantity)
                    {
                        _logger.LogWarning(
                            "Insufficient stock for inventory exit. ProductId {ProductId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} StockBefore {StockBefore} RequestedQuantity {RequestedQuantity}",
                            product.Id,
                            operationalContext.UserId,
                            operationalContext.CompanyId,
                            operationalContext.EstablishmentId,
                            stockBefore,
                            quantity);
                        throw new InvalidOperationException("INSUFFICIENT_STOCK");
                    }
                    stockAfter = stockBefore - quantity;
                    break;
                case InventoryMovementType.Sale:
                    if (stockBefore < quantity)
                    {
                        _logger.LogWarning(
                            "Insufficient stock for sale movement. ProductId {ProductId} UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} StockBefore {StockBefore} RequestedQuantity {RequestedQuantity}",
                            product.Id,
                            operationalContext.UserId,
                            operationalContext.CompanyId,
                            operationalContext.EstablishmentId,
                            stockBefore,
                            quantity);
                        throw new InvalidOperationException("INSUFFICIENT_STOCK");
                    }
                    stockAfter = stockBefore - quantity;
                    break;
                case InventoryMovementType.Void:
                    stockAfter = stockBefore + quantity;
                    break;
                case InventoryMovementType.Adjustment:
                    stockAfter = quantity;
                    break;
                default:
                    throw new InvalidOperationException("INVALID_MOVEMENT_TYPE");
            }

            if (productStock is null)
            {
                throw new InvalidOperationException("INVENTORY_CONCURRENCY_CONFLICT");
            }

            productStock.Quantity = stockAfter;
            productStock.UpdatedAt = DateTime.UtcNow;

            var movement = new InventoryMovement
            {
                ProductId = product.Id,
                CompanyId = operationalContext.CompanyId,
                EstablishmentId = operationalContext.EstablishmentId,
                Type = type,
                SourceType = sourceType,
                SourceId = sourceId,
                SourceLineId = sourceLineId,
                Quantity = quantity,
                StockBefore = stockBefore,
                StockAfter = stockAfter,
                Reference = reference?.Trim(),
                Notes = notes?.Trim(),
                UserId = operationalContext.UserId,
                CreatedAt = DateTime.UtcNow
            };

            _context.InventoryMovements.Add(movement);

            await _context.SaveChangesAsync();
            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            return new InventoryMovementDto
            {
                Id = movement.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Type = movement.Type,
                SourceType = movement.SourceType,
                SourceId = movement.SourceId,
                SourceLineId = movement.SourceLineId,
                Quantity = movement.Quantity,
                StockBefore = movement.StockBefore,
                StockAfter = movement.StockAfter,
                Reference = movement.Reference,
                Notes = movement.Notes,
                UserId = movement.UserId,
                CreatedAt = movement.CreatedAt
            };
        }
        catch (Exception ex) when (IsConcurrencyFailure(ex))
        {
            _logger.LogWarning(
                ex,
                "Inventory concurrency conflict. ProductId {ProductId} MovementType {MovementType} Quantity {Quantity}",
                productId,
                type,
                quantity);
            throw new InvalidOperationException("INVENTORY_CONCURRENCY_CONFLICT", ex);
        }
    }

    private async Task<Product> GetValidProductAsync(int productId, int companyId)
    {
        var product = await GetProductInCompanyAsync(productId, companyId);

        if (!product.IsActive)
        {
            throw new InvalidOperationException("PRODUCT_INACTIVE");
        }

        return product;
    }

    private async Task<Product> GetProductInCompanyAsync(int productId, int companyId)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.CompanyId == companyId);

        if (product is null)
        {
            throw new KeyNotFoundException("PRODUCT_NOT_FOUND");
        }

        return product;
    }

    private IQueryable<InventoryMovement> BuildMovementQuery(int companyId, int establishmentId)
    {
        return _context.InventoryMovements
            .Where(m => m.CompanyId == companyId
                && m.EstablishmentId == establishmentId);
    }

    private Task<ProductStock?> GetLockedProductStockAsync(int productId, int companyId, int establishmentId)
    {
        return _context.ProductStocks
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""ProductStocks""
                WHERE ""ProductId"" = {productId}
                  AND ""CompanyId"" = {companyId}
                  AND ""EstablishmentId"" = {establishmentId}
                FOR UPDATE")
            .SingleOrDefaultAsync();
    }

    private async Task<ProductStock> CreateAndLockProductStockAsync(int productId, int companyId, int establishmentId)
    {
        var productStock = new ProductStock
        {
            ProductId = productId,
            CompanyId = companyId,
            EstablishmentId = establishmentId,
            Quantity = 0m,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ProductStocks.Add(productStock);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _context.Entry(productStock).State = EntityState.Detached;
        }

        var lockedProductStock = await GetLockedProductStockAsync(productId, companyId, establishmentId);
        return lockedProductStock ?? throw new InvalidOperationException("INVENTORY_CONCURRENCY_CONFLICT");
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        return exception switch
        {
            DbUpdateException dbUpdateException when dbUpdateException.InnerException is PostgresException postgresException
                && (postgresException.SqlState == PostgresErrorCodes.UniqueViolation
                    || postgresException.SqlState == PostgresErrorCodes.DeadlockDetected
                    || postgresException.SqlState == PostgresErrorCodes.SerializationFailure
                    || postgresException.SqlState == PostgresErrorCodes.LockNotAvailable) => true,
            PostgresException postgresException when postgresException.SqlState == PostgresErrorCodes.DeadlockDetected
                || postgresException.SqlState == PostgresErrorCodes.SerializationFailure
                || postgresException.SqlState == PostgresErrorCodes.LockNotAvailable => true,
            _ => false
        };
    }
}
