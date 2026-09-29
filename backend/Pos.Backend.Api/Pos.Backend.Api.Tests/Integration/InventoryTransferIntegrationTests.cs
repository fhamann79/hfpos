using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class InventoryTransferIntegrationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_product_moves_atomically_and_records_two_linked_movements()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-one", 100, 8m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var transfer = await services.Transfers.CreateAsync(Request(destination.EstablishmentId,
            (tenant.Products[0].Id, 3m)));

        Assert.Equal(tenant.EstablishmentId, transfer.SourceEstablishmentId);
        Assert.Equal(destination.EstablishmentId, transfer.DestinationEstablishmentId);
        Assert.Equal(1, transfer.LineCount);
        Assert.Equal(3m, transfer.TotalQuantity);
        Assert.Equal("America/Guayaquil", transfer.TimeZoneIdSnapshot);
        Assert.Single(transfer.Items);
        await using var verify = database.CreateDbContext();
        Assert.Equal(5m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(3m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[0].Id));
        var movements = await verify.InventoryMovements.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(InventoryMovementType.Exit, movements[0].Type);
        Assert.Equal(InventoryMovementSourceType.InventoryTransferOut, movements[0].SourceType);
        Assert.Equal(InventoryMovementType.Entry, movements[1].Type);
        Assert.Equal(InventoryMovementSourceType.InventoryTransferIn, movements[1].SourceType);
        Assert.All(movements, m => Assert.Equal(transfer.Id, m.SourceId));
        Assert.All(movements, m => Assert.Equal(transfer.Items[0].Id, m.SourceLineId));
        Assert.Equal(movements[0].Id, transfer.Items[0].SourceMovementId);
        Assert.Equal(movements[1].Id, transfer.Items[0].DestinationMovementId);
        Assert.Equal(8m, 5m + 3m);
    }

    [Fact]
    public async Task Multiple_products_move_together_and_destination_rows_are_created()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-many", 101, 8m, 6m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var transfer = await services.Transfers.CreateAsync(Request(destination.EstablishmentId,
            (tenant.Products[0].Id, 2m), (tenant.Products[1].Id, 4m)));
        Assert.Equal(2, transfer.LineCount);
        await using var verify = database.CreateDbContext();
        Assert.Equal(6m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(2m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[1].Id));
        Assert.Equal(2m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(4m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[1].Id));
        Assert.Equal(4, await verify.InventoryMovements.CountAsync());
        Assert.Equal(8m, 6m + 2m);
        Assert.Equal(6m, 2m + 4m);
    }

    [Fact]
    public async Task Insufficient_second_line_rolls_back_every_effect()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-rollback", 102, 8m, 1m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Transfers.CreateAsync(Request(destination.EstablishmentId,
                (tenant.Products[0].Id, 2m), (tenant.Products[1].Id, 2m))));
        Assert.Equal("INVENTORY_TRANSFER_INSUFFICIENT_STOCK", error.Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(8m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(1m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[1].Id));
        Assert.Equal(0, await verify.ProductStocks.CountAsync(s => s.EstablishmentId == destination.EstablishmentId));
        Assert.Empty(await verify.InventoryTransfers.ToListAsync());
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Missing_source_stock_is_zero_and_does_not_create_a_source_row()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-missing", 112, 0m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using (var setup = database.CreateDbContext())
        {
            var source = await setup.ProductStocks.SingleAsync(s =>
                s.EstablishmentId == tenant.EstablishmentId && s.ProductId == tenant.Products[0].Id);
            setup.ProductStocks.Remove(source);
            await setup.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 1m))));
        Assert.Equal("INVENTORY_TRANSFER_INSUFFICIENT_STOCK", error.Message);
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.ProductStocks.ToListAsync());
        Assert.Empty(await verify.InventoryTransfers.ToListAsync());
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Destination_validation_and_cross_tenant_product_leave_no_effects()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-valid", 103, 5m);
        var foreign = await TestDataBuilder.CreateTenantAsync(database, "transfer-foreign", 104, 5m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        var inactive = await AddEstablishmentAsync(tenant, "C", false);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var failures = new (InventoryTransferCreateDto Request, string Code)[]
        {
            (new InventoryTransferCreateDto { DestinationEstablishmentId = destination.EstablishmentId,
                RequestId = Guid.NewGuid() }, "INVENTORY_TRANSFER_ITEMS_REQUIRED"),
            (Request(tenant.EstablishmentId, (tenant.Products[0].Id, 1m)), "INVENTORY_TRANSFER_SAME_ESTABLISHMENT"),
            (Request(inactive.EstablishmentId, (tenant.Products[0].Id, 1m)), "INVENTORY_TRANSFER_DESTINATION_INACTIVE"),
            (Request(foreign.EstablishmentId, (tenant.Products[0].Id, 1m)), "INVENTORY_TRANSFER_DESTINATION_NOT_FOUND"),
            (Request(destination.EstablishmentId, (foreign.Products[0].Id, 1m)), "PRODUCT_NOT_FOUND"),
            (Request(destination.EstablishmentId, (tenant.Products[0].Id, 0m)), "INVENTORY_TRANSFER_QUANTITY_INVALID"),
            (Request(destination.EstablishmentId, (tenant.Products[0].Id, -1m)), "INVENTORY_TRANSFER_QUANTITY_INVALID"),
            (Request(destination.EstablishmentId, (tenant.Products[0].Id, 1m), (tenant.Products[0].Id, 1m)), "INVENTORY_TRANSFER_DUPLICATE_PRODUCT")
        };
        foreach (var (request, code) in failures)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => services.Transfers.CreateAsync(request));
            Assert.Equal(code, error.Message);
        }
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.InventoryTransfers.ToListAsync());
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
        Assert.Equal(5m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Inactive_product_with_stock_can_transfer()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-inactive", 105, 5m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using (var context = database.CreateDbContext())
        {
            var product = await context.Products.SingleAsync(p => p.Id == tenant.Products[0].Id);
            product.IsActive = false;
            await context.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        await services.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 2m)));
        await using var verify = database.CreateDbContext();
        Assert.Equal(3m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(2m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Sequential_retry_returns_same_document_without_moving_stock_twice()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-retry", 106, 5m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        var request = Request(destination.EstablishmentId, (tenant.Products[0].Id, 2m));
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.Transfers.CreateAsync(request);
        var second = await services.Transfers.CreateAsync(request);
        Assert.Equal(first.Id, second.Id);
        Assert.False(first.WasAlreadyProcessed);
        Assert.True(second.WasAlreadyProcessed);
        request.Items[0].Quantity = 3m;
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Transfers.CreateAsync(request));
        Assert.Equal("INVENTORY_TRANSFER_REQUEST_CONFLICT", conflict.Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.InventoryTransfers.CountAsync());
        Assert.Equal(2, await verify.InventoryMovements.CountAsync());
        Assert.Equal(3m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(2m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Concurrent_same_request_has_one_effect()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-race-id", 107, 5m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        var request = Request(destination.EstablishmentId, (tenant.Products[0].Id, 2m));
        await using var left = new TestServiceScope(database, tenant.OperationalContext);
        await using var right = new TestServiceScope(database, tenant.OperationalContext);
        var results = await Task.WhenAll(left.Transfers.CreateAsync(request),
            right.Transfers.CreateAsync(request)).WaitAsync(Timeout);
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Single(results.Where(result => result.WasAlreadyProcessed));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.InventoryTransfers.CountAsync());
        Assert.Equal(2, await verify.InventoryMovements.CountAsync());
        Assert.Equal(3m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Concurrent_different_requests_cannot_overdraw_source()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-race-stock", 108, 5m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        await using var left = new TestServiceScope(database, tenant.OperationalContext);
        await using var right = new TestServiceScope(database, tenant.OperationalContext);
        var outcomes = await Task.WhenAll(
            CaptureAsync(left.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 4m)))),
            CaptureAsync(right.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 4m)))))
            .WaitAsync(Timeout);
        Assert.Single(outcomes.Where(o => o.Result is not null));
        Assert.Single(outcomes.Where(o => o.Error?.Message == "INVENTORY_TRANSFER_INSUFFICIENT_STOCK"));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(4m, await StockAsync(verify, destination.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(1, await verify.InventoryTransfers.CountAsync());
    }

    [Fact]
    public async Task Opposite_direction_transfers_conserve_stock_without_deadlock()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-opposite", 109, 5m);
        var branch = await AddEstablishmentAsync(tenant, "B", initialProductId: tenant.Products[0].Id, initialStock: 5m);
        await using var left = new TestServiceScope(database, tenant.OperationalContext);
        await using var right = new TestServiceScope(database, branch);
        await Task.WhenAll(
            left.Transfers.CreateAsync(Request(branch.EstablishmentId, (tenant.Products[0].Id, 2m))),
            right.Transfers.CreateAsync(Request(tenant.EstablishmentId, (tenant.Products[0].Id, 3m))))
            .WaitAsync(Timeout);
        await using var verify = database.CreateDbContext();
        Assert.Equal(6m, await StockAsync(verify, tenant.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(4m, await StockAsync(verify, branch.EstablishmentId, tenant.Products[0].Id));
        Assert.Equal(10m, 6m + 4m);
    }

    [Fact]
    public async Task Reads_are_participant_scoped_and_pagination_filters_before_count()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "transfer-read", 110, 9m);
        var destination = await AddEstablishmentAsync(tenant, "B");
        var unrelated = await AddEstablishmentAsync(tenant, "C");
        var foreign = await TestDataBuilder.CreateTenantAsync(database, "transfer-read-foreign", 111, 5m);
        await using var sourceScope = new TestServiceScope(database, tenant.OperationalContext);
        var first = await sourceScope.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 1m)));
        var second = await sourceScope.Transfers.CreateAsync(Request(destination.EstablishmentId, (tenant.Products[0].Id, 1m)));
        Assert.NotNull(await sourceScope.Transfers.GetByIdAsync(first.Id));
        await using var destinationScope = new TestServiceScope(database, destination);
        Assert.NotNull(await destinationScope.Transfers.GetByIdAsync(first.Id));
        await using var unrelatedScope = new TestServiceScope(database, unrelated);
        Assert.Null(await unrelatedScope.Transfers.GetByIdAsync(first.Id));
        Assert.Equal(0, (await unrelatedScope.Transfers.GetAsync(new InventoryTransferQueryDto())).TotalItems);
        await using var foreignScope = new TestServiceScope(database, foreign.OperationalContext);
        Assert.Null(await foreignScope.Transfers.GetByIdAsync(first.Id));
        var page = await sourceScope.Transfers.GetAsync(new InventoryTransferQueryDto { PageSize = 1 });
        Assert.Equal(2, page.TotalItems);
        Assert.Equal(second.Id, page.Items[0].Id);
        var filtered = await sourceScope.Transfers.GetAsync(new InventoryTransferQueryDto
        {
            PageSize = 999, Search = "not-present"
        });
        Assert.Equal(0, filtered.TotalItems);
        Assert.Equal(200, filtered.PageSize);
    }

    private static InventoryTransferCreateDto Request(int destinationId, params (int ProductId, decimal Quantity)[] items) =>
        new()
        {
            DestinationEstablishmentId = destinationId,
            RequestId = Guid.NewGuid(),
            Reference = "TEST-TRANSFER",
            Items = items.Select(i => new InventoryTransferCreateItemDto
            { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
        };

    private async Task<OperationalContext> AddEstablishmentAsync(
        TestTenant tenant, string code, bool active = true, int? initialProductId = null, decimal initialStock = 0m)
    {
        await using var context = database.CreateDbContext();
        var establishment = new Establishment
        {
            CompanyId = tenant.CompanyId, Code = code == "B" ? "002" : "003",
            Name = $"Branch {code}", Address = "Synthetic address",
            IsActive = active, CreatedAt = DateTime.UtcNow
        };
        context.Establishments.Add(establishment);
        await context.SaveChangesAsync();
        var emission = new EmissionPoint
        {
            EstablishmentId = establishment.Id, Code = "001", Name = "Test point",
            IsActive = active, CreatedAt = DateTime.UtcNow
        };
        context.EmissionPoints.Add(emission);
        await context.SaveChangesAsync();
        var roleId = await context.Roles.Where(r => r.CompanyId == tenant.CompanyId).Select(r => r.Id).FirstAsync();
        var user = new User
        {
            CompanyId = tenant.CompanyId, EstablishmentId = establishment.Id,
            EmissionPointId = emission.Id, RoleId = roleId,
            Username = $"transfer-{tenant.CompanyId}-{code}",
            Email = $"transfer-{tenant.CompanyId}-{code}@hfpos.test",
            PasswordHash = "test-only-password-hash", IsActive = active,
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(user);
        if (initialProductId.HasValue)
            context.ProductStocks.Add(new ProductStock
            {
                CompanyId = tenant.CompanyId, EstablishmentId = establishment.Id,
                ProductId = initialProductId.Value, Quantity = initialStock, UpdatedAt = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        return new OperationalContext
        {
            CompanyId = tenant.CompanyId, EstablishmentId = establishment.Id,
            EmissionPointId = emission.Id, UserId = user.Id,
            Username = user.Username, CompanyTimeZoneId = "America/Guayaquil"
        };
    }

    private static Task<decimal> StockAsync(Microsoft.EntityFrameworkCore.DbContext context, int establishmentId, int productId) =>
        context.Set<ProductStock>().Where(s => s.EstablishmentId == establishmentId && s.ProductId == productId)
            .Select(s => s.Quantity).SingleAsync();

    private static async Task<(InventoryTransferDetailDto? Result, Exception? Error)> CaptureAsync(
        Task<InventoryTransferDetailDto> task)
    {
        try { return (await task, null); }
        catch (Exception ex) { return (null, ex); }
    }
}
