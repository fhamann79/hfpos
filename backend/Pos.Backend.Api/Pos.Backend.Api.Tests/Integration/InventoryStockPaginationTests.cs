using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class InventoryStockPaginationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pages_are_stable_and_summary_covers_the_whole_filtered_result()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock-page", 521, 5m, 0m, 2m, 8m);
        await using (var context = database.CreateDbContext())
        {
            var products = await context.Products.OrderBy(p => p.Id).ToListAsync();
            foreach (var product in products) product.Name = "Same name";
            products[3].IsActive = false;
            await context.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.Inventory.GetStocksAsync(null, null, false, 1, 2);
        var second = await services.Inventory.GetStocksAsync(null, null, false, 2, 2);
        Assert.Equal(tenant.Products.Take(2).Select(p => p.Id), first.Items.Select(p => p.ProductId));
        Assert.Equal(tenant.Products.Skip(2).Select(p => p.Id), second.Items.Select(p => p.ProductId));
        Assert.Equal(4, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, second.Page);
        Assert.Equal(4, first.Summary.TotalProducts);
        Assert.Equal(1, first.Summary.OutOfStockProducts);
        Assert.Equal(1, first.Summary.LowStockProducts);
        Assert.Equal(1, first.Summary.InactiveProducts);
        Assert.Equal(15m, first.Summary.TotalInventoryUnits);
        Assert.Equal(30m, first.Summary.TotalInventoryValue);
        Assert.Equal(15m, second.Summary.TotalInventoryUnits);
    }

    [Theory]
    [InlineData(" STOCK-FILTER ", 3)]
    [InlineData("product stock-filter-2", 1)]
    [InlineData("category stock-filter", 3)]
    [InlineData("missing", 0)]
    public async Task Search_filters_before_count_and_summary(string search, int expected)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock-filter", 522, 5m, 0m, 2m);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var result = await services.Inventory.GetStocksAsync(search, null, false, 1, 1);
        Assert.Equal(expected, result.TotalItems);
        Assert.Equal(expected, result.Summary.TotalProducts);
        Assert.Equal(Math.Min(expected, 1), result.Items.Count);
    }

    [Fact]
    public async Task Product_and_positive_filters_are_combined_before_count()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock-positive", 523, 5m, 0m, 2m);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var positive = await services.Inventory.GetStocksAsync(null, null, true, 1, 1);
        Assert.Equal(2, positive.TotalItems);
        Assert.Equal(7m, positive.Summary.TotalInventoryUnits);
        var selected = await services.Inventory.GetStocksAsync(null, tenant.Products[2].Id, true);
        Assert.Equal(2m, Assert.Single(selected.Items).Quantity);
        var zero = await services.Inventory.GetStocksAsync(null, tenant.Products[1].Id, true);
        Assert.Empty(zero.Items);
        Assert.Equal(0, zero.TotalItems);
        Assert.Equal(0m, zero.Summary.TotalInventoryValue);
    }

    [Fact]
    public async Task Bounds_defaults_and_empty_pages_do_not_materialize_full_results()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock-bounds", 524,
            Enumerable.Repeat(1m, 205).ToArray());
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var defaults = await services.Inventory.GetStocksAsync(null, null, false);
        Assert.Equal(30, defaults.Items.Count);
        var maximum = await services.Inventory.GetStocksAsync(null, null, false, 0, 1000);
        Assert.Equal(1, maximum.Page);
        Assert.Equal(200, maximum.PageSize);
        Assert.Equal(200, maximum.Items.Count);
        Assert.Equal(205, maximum.TotalItems);
        var minimum = await services.Inventory.GetStocksAsync(null, null, false, -5, 0);
        Assert.Single(minimum.Items);
        Assert.Equal(1, minimum.PageSize);
        var empty = await services.Inventory.GetStocksAsync(null, null, false, int.MaxValue, 200);
        Assert.Empty(empty.Items);
        Assert.Equal(205, empty.TotalItems);
    }

    [Fact]
    public async Task Company_and_establishment_isolate_page_summary_and_transfer_lookup()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "stock-local", 525, 5m, 0m);
        var foreign = await TestDataBuilder.CreateTenantAsync(database, "stock-foreign", 526, 90m);
        int otherId;
        await using (var context = database.CreateDbContext())
        {
            var establishment = new Establishment { CompanyId = tenant.CompanyId, Code = "002",
                Name = "Synthetic other", Address = "Synthetic", IsActive = true };
            context.Establishments.Add(establishment);
            await context.SaveChangesAsync();
            otherId = establishment.Id;
            context.ProductStocks.Add(new ProductStock { CompanyId = tenant.CompanyId,
                EstablishmentId = otherId, ProductId = tenant.Products[1].Id, Quantity = 77m });
            await context.SaveChangesAsync();
        }
        await using var local = new TestServiceScope(database, tenant.OperationalContext);
        var page = await local.Inventory.GetStocksAsync(null, null, false);
        Assert.Equal(2, page.TotalItems);
        Assert.Equal(5m, page.Summary.TotalInventoryUnits);
        Assert.Equal(0m, page.Items.Single(p => p.ProductId == tenant.Products[1].Id).Quantity);
        Assert.Empty((await local.Inventory.GetStocksAsync(null, foreign.Products[0].Id, false)).Items);
        Assert.Equal(tenant.Products[0].Id, Assert.Single(await local.Inventory.GetTransferLookupAsync(null)).ProductId);
        Assert.Empty(await local.Inventory.GetTransferLookupAsync("stock-foreign"));
        await using var other = new TestServiceScope(database, new OperationalContext {
            CompanyId = tenant.CompanyId, EstablishmentId = otherId,
            EmissionPointId = tenant.EmissionPointId, UserId = tenant.UserId,
            Username = tenant.OperationalContext.Username, CompanyTimeZoneId = "America/Guayaquil" });
        Assert.Equal(77m, (await other.Inventory.GetStocksAsync(null, null, false)).Summary.TotalInventoryUnits);
        Assert.Equal(tenant.Products[1].Id, Assert.Single(await other.Inventory.GetTransferLookupAsync(null)).ProductId);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(null, 1000)]
    [InlineData(null, 0)]
    [InlineData(" barcode-target ", 30)]
    [InlineData(" INTERNAL-TARGET ", 30)]
    [InlineData("inactive target", 30)]
    public async Task Transfer_lookup_is_bounded_searchable_and_includes_inactive_positive_stock(string? search, int take)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "lookup", 527,
            Enumerable.Repeat(5m, 105).Append(0m).ToArray());
        await using (var context = database.CreateDbContext())
        {
            var target = await context.Products.SingleAsync(p => p.Id == tenant.Products[104].Id);
            target.Name = "ZZ inactive target";
            target.IsActive = false;
            target.Barcode = "barcode-target";
            target.InternalCode = "internal-target";
            await context.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var results = await services.Inventory.GetTransferLookupAsync(search, take);
        Assert.Equal(search is null ? Math.Clamp(take, 1, 100) : 1, results.Count);
        Assert.All(results, p => Assert.True(p.Quantity > 0));
        if (search is not null)
        {
            var target = Assert.Single(results);
            Assert.False(target.IsActive);
            Assert.Equal(tenant.Products[104].Id, target.ProductId);
            Assert.Equal("barcode-target", target.Barcode);
            var page = await services.Inventory.GetStocksAsync(null, null, false, 1, 1);
            Assert.DoesNotContain(page.Items, p => p.ProductId == target.ProductId);
        }
    }

    [Fact]
    public void Transfer_lookup_requires_inventory_read_without_catalog_permission()
    {
        var method = typeof(InventoryController).GetMethod(nameof(InventoryController.GetTransferProducts))!;
        var policies = method.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>().Select(a => a.Policy).ToArray();
        Assert.Equal([AppPermissions.InventoryRead], policies);
    }
}
