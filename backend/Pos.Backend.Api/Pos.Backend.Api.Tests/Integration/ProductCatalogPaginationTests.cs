using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ProductCatalogPaginationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Product_page_is_tenant_scoped_searchable_stable_and_bounded()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "product-page", 221, 0m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "product-other", 222, 0m);

        await using (var context = database.CreateDbContext())
        {
            var existing = await context.Products.SingleAsync(product => product.Id == tenant.Products[0].Id);
            existing.Name = "Bravo Product";
            existing.Barcode = "BAR-002";
            existing.InternalCode = "INT-002";

            var categoryId = existing.CategoryId;
            context.Products.AddRange(
                Product(tenant.CompanyId, categoryId, "Alpha Product", "BAR-001", "INT-001", true, 11m),
                Product(tenant.CompanyId, categoryId, "Inactive Product", "BAR-003", "INT-003", false, 13m));

            var foreign = await context.Products.SingleAsync(product => product.Id == otherTenant.Products[0].Id);
            foreign.Name = "Foreign Product";
            foreign.Barcode = "BAR-X";
            foreign.InternalCode = "INT-X";
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateDbContext();
        var controller = new ProductsController(
            readContext,
            new FixedOperationalContextAccessor(tenant.OperationalContext),
            lifecycle: null!,
            administrationGuard: null!,
            productCostService: null!);

        var first = await PageAsync(controller.GetPage(null, "all", null, page: 1, pageSize: 2));
        var second = await PageAsync(controller.GetPage(null, "all", null, page: 2, pageSize: 2));
        var barcode = await PageAsync(controller.GetPage("BAR-001", "all", null, page: 1, pageSize: 20));
        var internalCode = await PageAsync(controller.GetPage("INT-002", "all", null, page: 1, pageSize: 20));
        var maximum = await PageAsync(controller.GetPage(null, "all", null, page: 1, pageSize: 999));

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new[] { "Alpha Product", "Bravo Product" }, first.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Inactive Product" }, second.Items.Select(item => item.Name));
        Assert.Single(barcode.Items);
        Assert.Equal("Alpha Product", barcode.Items[0].Name);
        Assert.Single(internalCode.Items);
        Assert.Equal("Bravo Product", internalCode.Items[0].Name);
        Assert.Equal(200, maximum.PageSize);
        Assert.Equal(3, maximum.Items.Count);
        Assert.DoesNotContain(first.Items.Concat(second.Items), item => item.Name == "Foreign Product");
    }

    [Fact]
    public async Task Product_lookup_returns_only_active_tenant_products_and_respects_take_bound()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "product-lookup", 223, 0m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "product-lookup-other", 224, 0m);

        await using (var context = database.CreateDbContext())
        {
            var existing = await context.Products.SingleAsync(product => product.Id == tenant.Products[0].Id);
            existing.Name = "Alpha Lookup";
            existing.InternalCode = "LOOK-001";

            context.Products.Add(new Product
            {
                CompanyId = tenant.CompanyId,
                CategoryId = existing.CategoryId,
                Name = "Inactive Lookup",
                InternalCode = "LOOK-002",
                Price = 10m,
                Cost = 2m,
                MinimumStock = 0m,
                VatCategory = ProductVatCategory.Vat0,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });

            var foreign = await context.Products.SingleAsync(product => product.Id == otherTenant.Products[0].Id);
            foreign.Name = "Foreign Lookup";
            foreign.InternalCode = "LOOK-X";
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateDbContext();
        var controller = new ProductsController(
            readContext,
            new FixedOperationalContextAccessor(tenant.OperationalContext),
            lifecycle: null!,
            administrationGuard: null!,
            productCostService: null!);

        var action = await controller.Lookup("LOOK", take: 999, categoryId: null);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<ProductDto>>(ok.Value).ToArray();

        Assert.Single(items);
        Assert.Equal("Alpha Lookup", items[0].Name);
        Assert.DoesNotContain(items, item => item.Name is "Inactive Lookup" or "Foreign Lookup");
    }

    private static Product Product(
        int companyId,
        int categoryId,
        string name,
        string barcode,
        string internalCode,
        bool active,
        decimal price)
        => new()
        {
            CompanyId = companyId,
            CategoryId = categoryId,
            Name = name,
            Barcode = barcode,
            InternalCode = internalCode,
            Price = price,
            Cost = 2m,
            MinimumStock = 0m,
            VatCategory = ProductVatCategory.Vat0,
            IsActive = active,
            CreatedAt = DateTime.UtcNow
        };

    private static async Task<PagedResultDto<ProductDto>> PageAsync(Task<ActionResult<PagedResultDto<ProductDto>>> task)
    {
        var action = await task;
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<PagedResultDto<ProductDto>>(ok.Value);
    }

    private sealed class FixedOperationalContextAccessor(OperationalContext context) : IOperationalContextAccessor
    {
        public Task<OperationalContext> GetRequiredContextAsync() => Task.FromResult(context);
    }
}
