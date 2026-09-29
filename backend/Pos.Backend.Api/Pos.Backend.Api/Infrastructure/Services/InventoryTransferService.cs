using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class InventoryTransferService(
    PosDbContext context,
    IOperationalContextAccessor operationalContextAccessor,
    IBusinessClockService businessClock,
    TenantAdministrationGuard administrationGuard) : IInventoryTransferService
{
    public async Task<InventoryTransferDetailDto> CreateAsync(InventoryTransferCreateDto request)
    {
        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();
        ValidateRequest(request, operationalContext);
        var reference = Normalize(request.Reference);
        var notes = Normalize(request.Notes);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await administrationGuard.LockOperationalWriteAsync(operationalContext);

            var existing = await FindByRequestIdAsync(operationalContext, request.RequestId);
            if (existing is not null)
            {
                EnsureSameRequest(existing, request, reference, notes);
                existing.WasAlreadyProcessed = true;
                return existing;
            }

            var destination = await context.Establishments.AsNoTracking()
                .Where(e => e.Id == request.DestinationEstablishmentId
                    && e.CompanyId == operationalContext.CompanyId)
                .Select(e => new { e.Id, e.IsActive })
                .SingleOrDefaultAsync();
            if (destination is null)
                throw new KeyNotFoundException("INVENTORY_TRANSFER_DESTINATION_NOT_FOUND");
            if (!destination.IsActive)
                throw new InvalidOperationException("INVENTORY_TRANSFER_DESTINATION_INACTIVE");

            var productIds = request.Items.Select(i => i.ProductId).ToArray();
            var validProductCount = await context.Products.AsNoTracking()
                .CountAsync(p => p.CompanyId == operationalContext.CompanyId && productIds.Contains(p.Id));
            if (validProductCount != productIds.Length)
                throw new KeyNotFoundException("PRODUCT_NOT_FOUND");

            var now = businessClock.UtcNow;
            var transfer = new InventoryTransfer
            {
                CompanyId = operationalContext.CompanyId,
                SourceEstablishmentId = operationalContext.EstablishmentId,
                DestinationEstablishmentId = destination.Id,
                CreatedByUserId = operationalContext.UserId,
                CreatedAt = now,
                BusinessDate = businessClock.GetBusinessDate(now, operationalContext.CompanyTimeZoneId),
                TimeZoneIdSnapshot = operationalContext.CompanyTimeZoneId,
                Reference = reference,
                Notes = notes,
                RequestId = request.RequestId
            };
            context.InventoryTransfers.Add(transfer);
            await context.SaveChangesAsync();

            var stockKeys = productIds
                .SelectMany(productId => new[]
                {
                    (EstablishmentId: operationalContext.EstablishmentId, ProductId: productId),
                    (EstablishmentId: destination.Id, ProductId: productId)
                })
                .OrderBy(key => key.EstablishmentId)
                .ThenBy(key => key.ProductId)
                .ToArray();
            var lockedStocks = new Dictionary<(int EstablishmentId, int ProductId), ProductStock?>();

            foreach (var key in stockKeys)
            {
                if (key.EstablishmentId == destination.Id)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO ""ProductStocks"" (""ProductId"", ""CompanyId"", ""EstablishmentId"", ""Quantity"", ""UpdatedAt"")
                        VALUES ({key.ProductId}, {operationalContext.CompanyId}, {key.EstablishmentId}, 0, {now})
                        ON CONFLICT (""ProductId"", ""CompanyId"", ""EstablishmentId"") DO NOTHING");
                }

                var stock = await context.ProductStocks.FromSqlInterpolated($@"
                    SELECT * FROM ""ProductStocks""
                    WHERE ""ProductId"" = {key.ProductId}
                      AND ""CompanyId"" = {operationalContext.CompanyId}
                      AND ""EstablishmentId"" = {key.EstablishmentId}
                    FOR UPDATE").SingleOrDefaultAsync();
                lockedStocks.Add(key, stock);
            }

            foreach (var item in request.Items)
            {
                var source = lockedStocks[(operationalContext.EstablishmentId, item.ProductId)];
                if (source is null || source.Quantity < item.Quantity)
                    throw new InvalidOperationException("INVENTORY_TRANSFER_INSUFFICIENT_STOCK");
            }

            foreach (var item in request.Items)
            {
                var source = lockedStocks[(operationalContext.EstablishmentId, item.ProductId)]!;
                var target = lockedStocks[(destination.Id, item.ProductId)]!;
                var sourceBefore = source.Quantity;
                var targetBefore = target.Quantity;
                source.Quantity -= item.Quantity;
                target.Quantity += item.Quantity;
                source.UpdatedAt = now;
                target.UpdatedAt = now;

                var exit = NewMovement(transfer, item, InventoryMovementType.Exit,
                    InventoryMovementSourceType.InventoryTransferOut,
                    operationalContext.EstablishmentId, sourceBefore, source.Quantity);
                var entry = NewMovement(transfer, item, InventoryMovementType.Entry,
                    InventoryMovementSourceType.InventoryTransferIn,
                    destination.Id, targetBefore, target.Quantity);
                context.InventoryMovements.AddRange(exit, entry);
                await context.SaveChangesAsync();

                var line = new InventoryTransferItem
                {
                    InventoryTransferId = transfer.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    SourceMovementId = exit.Id,
                    DestinationMovementId = entry.Id
                };
                context.InventoryTransferItems.Add(line);
                await context.SaveChangesAsync();
                exit.SourceLineId = line.Id;
                entry.SourceLineId = line.Id;
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return (await GetByIdAsync(transfer.Id))!;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation,
              ConstraintName: "IX_InventoryTransfers_CompanyId_RequestId" })
        {
            context.ChangeTracker.Clear();
            var existing = await FindByRequestIdAsync(operationalContext, request.RequestId);
            if (existing is null)
                throw new InvalidOperationException("INVENTORY_TRANSFER_CONCURRENCY_CONFLICT", ex);
            EnsureSameRequest(existing, request, reference, notes);
            existing.WasAlreadyProcessed = true;
            return existing;
        }
        catch (Exception ex) when (IsConcurrencyFailure(ex))
        {
            throw new InvalidOperationException("INVENTORY_TRANSFER_CONCURRENCY_CONFLICT", ex);
        }
    }

    public async Task<IReadOnlyList<InventoryTransferDestinationDto>> GetDestinationsAsync()
    {
        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();
        return await context.Establishments.AsNoTracking()
            .Where(e => e.CompanyId == operationalContext.CompanyId
                && e.Id != operationalContext.EstablishmentId && e.IsActive)
            .OrderBy(e => e.Name).ThenBy(e => e.Id)
            .Select(e => new InventoryTransferDestinationDto { Id = e.Id, Name = e.Name })
            .ToListAsync();
    }

    public async Task<PagedResultDto<InventoryTransferListItemDto>> GetAsync(InventoryTransferQueryDto query)
    {
        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var transfers = VisibleTransfers(operationalContext);
        if (query.From.HasValue)
            transfers = transfers.Where(t => t.BusinessDate >= query.From.Value);
        if (query.To.HasValue)
            transfers = transfers.Where(t => t.BusinessDate <= query.To.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            transfers = transfers.Where(t =>
                (t.Reference != null && t.Reference.ToLower().Contains(term))
                || t.SourceEstablishment.Name.ToLower().Contains(term)
                || t.DestinationEstablishment.Name.ToLower().Contains(term));
        }

        var total = await transfers.CountAsync();
        var items = await transfers.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new InventoryTransferListItemDto
            {
                Id = t.Id,
                SourceEstablishmentId = t.SourceEstablishmentId,
                SourceEstablishmentName = t.SourceEstablishment.Name,
                DestinationEstablishmentId = t.DestinationEstablishmentId,
                DestinationEstablishmentName = t.DestinationEstablishment.Name,
                CreatedByUserId = t.CreatedByUserId,
                CreatedByUsername = t.CreatedByUser.Username,
                CreatedAt = t.CreatedAt,
                BusinessDate = t.BusinessDate,
                TimeZoneIdSnapshot = t.TimeZoneIdSnapshot,
                Reference = t.Reference,
                LineCount = t.Items.Count,
                TotalQuantity = t.Items.Sum(i => i.Quantity)
            }).ToListAsync();
        return new PagedResultDto<InventoryTransferListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<InventoryTransferDetailDto?> GetByIdAsync(int id)
    {
        var operationalContext = await operationalContextAccessor.GetRequiredContextAsync();
        var transfer = await VisibleTransfers(operationalContext)
            .Include(t => t.SourceEstablishment)
            .Include(t => t.DestinationEstablishment)
            .Include(t => t.CreatedByUser)
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .Include(t => t.Items).ThenInclude(i => i.SourceMovement)
            .Include(t => t.Items).ThenInclude(i => i.DestinationMovement)
            .AsSplitQuery()
            .SingleOrDefaultAsync(t => t.Id == id);
        return transfer is null ? null : MapDetail(transfer);
    }

    private IQueryable<InventoryTransfer> VisibleTransfers(OperationalContext operationalContext) =>
        context.InventoryTransfers.AsNoTracking().Where(t =>
            t.CompanyId == operationalContext.CompanyId
            && (t.SourceEstablishmentId == operationalContext.EstablishmentId
                || t.DestinationEstablishmentId == operationalContext.EstablishmentId));

    private async Task<InventoryTransferDetailDto?> FindByRequestIdAsync(
        OperationalContext operationalContext, Guid requestId)
    {
        var id = await VisibleTransfers(operationalContext)
            .Where(t => t.RequestId == requestId && t.SourceEstablishmentId == operationalContext.EstablishmentId)
            .Select(t => (int?)t.Id).SingleOrDefaultAsync();
        return id.HasValue ? await GetByIdAsync(id.Value) : null;
    }

    private static InventoryTransferDetailDto MapDetail(InventoryTransfer transfer) => new()
    {
        Id = transfer.Id,
        SourceEstablishmentId = transfer.SourceEstablishmentId,
        SourceEstablishmentName = transfer.SourceEstablishment.Name,
        DestinationEstablishmentId = transfer.DestinationEstablishmentId,
        DestinationEstablishmentName = transfer.DestinationEstablishment.Name,
        CreatedByUserId = transfer.CreatedByUserId,
        CreatedByUsername = transfer.CreatedByUser.Username,
        CreatedAt = transfer.CreatedAt,
        BusinessDate = transfer.BusinessDate,
        TimeZoneIdSnapshot = transfer.TimeZoneIdSnapshot,
        Reference = transfer.Reference,
        Notes = transfer.Notes,
        RequestId = transfer.RequestId,
        LineCount = transfer.Items.Count,
        TotalQuantity = transfer.Items.Sum(i => i.Quantity),
        Items = transfer.Items.OrderBy(i => i.Id).Select(i => new InventoryTransferItemDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            ProductName = i.Product.Name,
            Quantity = i.Quantity,
            SourceMovementId = i.SourceMovementId,
            DestinationMovementId = i.DestinationMovementId,
            SourceStockBefore = i.SourceMovement.StockBefore,
            SourceStockAfter = i.SourceMovement.StockAfter,
            DestinationStockBefore = i.DestinationMovement.StockBefore,
            DestinationStockAfter = i.DestinationMovement.StockAfter
        }).ToArray()
    };

    private static InventoryMovement NewMovement(
        InventoryTransfer transfer, InventoryTransferCreateItemDto item,
        InventoryMovementType type, InventoryMovementSourceType sourceType,
        int establishmentId, decimal before, decimal after)
    {
        return new InventoryMovement
        {
            CompanyId = transfer.CompanyId,
            EstablishmentId = establishmentId,
            ProductId = item.ProductId,
            Type = type,
            SourceType = sourceType,
            SourceId = transfer.Id,
            Quantity = item.Quantity,
            StockBefore = before,
            StockAfter = after,
            Reference = transfer.Reference,
            Notes = transfer.Notes,
            UserId = transfer.CreatedByUserId,
            BusinessDate = transfer.BusinessDate,
            TimeZoneIdSnapshot = transfer.TimeZoneIdSnapshot,
            CreatedAt = transfer.CreatedAt
        };
    }

    private static void ValidateRequest(InventoryTransferCreateDto request, OperationalContext operationalContext)
    {
        if (request.RequestId == Guid.Empty)
            throw new InvalidOperationException("INVENTORY_TRANSFER_REQUEST_ID_REQUIRED");
        if (request.DestinationEstablishmentId <= 0)
            throw new InvalidOperationException("INVENTORY_TRANSFER_DESTINATION_REQUIRED");
        if (request.DestinationEstablishmentId == operationalContext.EstablishmentId)
            throw new InvalidOperationException("INVENTORY_TRANSFER_SAME_ESTABLISHMENT");
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("INVENTORY_TRANSFER_ITEMS_REQUIRED");
        if (request.Items.Count > 200)
            throw new InvalidOperationException("INVENTORY_TRANSFER_ITEMS_INVALID");
        if (request.Items.Any(i => i.ProductId <= 0 || i.Quantity <= 0m
            || decimal.Round(i.Quantity, 4) != i.Quantity || i.Quantity > 99999999999999.9999m))
            throw new InvalidOperationException("INVENTORY_TRANSFER_QUANTITY_INVALID");
        if (request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count)
            throw new InvalidOperationException("INVENTORY_TRANSFER_DUPLICATE_PRODUCT");
        if (Normalize(request.Reference)?.Length > 100)
            throw new InvalidOperationException("INVENTORY_TRANSFER_REFERENCE_INVALID");
        if (Normalize(request.Notes)?.Length > 500)
            throw new InvalidOperationException("INVENTORY_TRANSFER_NOTES_INVALID");
    }

    private static void EnsureSameRequest(
        InventoryTransferDetailDto existing, InventoryTransferCreateDto request, string? reference, string? notes)
    {
        if (existing.DestinationEstablishmentId != request.DestinationEstablishmentId
            || existing.Reference != reference || existing.Notes != notes
            || existing.Items.Count != request.Items.Count
            || request.Items.Any(item => !existing.Items.Any(saved =>
                saved.ProductId == item.ProductId && saved.Quantity == item.Quantity)))
            throw new InvalidOperationException("INVENTORY_TRANSFER_REQUEST_CONFLICT");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsConcurrencyFailure(Exception exception) =>
        exception is PostgresException postgres
            && postgres.SqlState is PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable
        || exception is DbUpdateException { InnerException: PostgresException inner }
            && inner.SqlState is PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable
                or PostgresErrorCodes.UniqueViolation;
}
