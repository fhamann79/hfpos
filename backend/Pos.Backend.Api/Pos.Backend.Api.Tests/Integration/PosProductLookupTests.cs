using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PosProductLookupTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime CreatedAt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lookup_is_tenant_and_establishment_scoped_searchable_active_only_and_bounded()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "pos-lookup", 231, 7m, 4m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "pos-lookup-other", 232, 88m);

        int noStockProductId;

        await using (var context = database.CreateDbContext())
        {
            var first = await context.Products.SingleAsync(product => product.Id == tenant.Products[0].Id);
            first.Name = "Zulu exact product";
            first.Barcode = "SKU-EXACT";
            first.InternalCode = "INT-ZULU";

            var second = await context.Products.SingleAsync(product => product.Id == tenant.Products[1].Id);
            second.Name = "Alpha Search Product";
            second.Barcode = "BAR-ALPHA";
            second.InternalCode = "CODE-ALPHA";

            var foreign = await context.Products.SingleAsync(product => product.Id == otherTenant.Products[0].Id);
            foreign.Name = "Foreign Search Product";
            foreign.Barcode = "SKU-EXACT";

            var otherEstablishment = new Establishment
            {
                CompanyId = tenant.CompanyId,
                Code = "002",
                Name = "Other establishment",
                Address = "Other address",
                IsActive = true,
                CreatedAt = CreatedAt
            };
            context.Establishments.Add(otherEstablishment);
            await context.SaveChangesAsync();

            context.ProductStocks.Add(new ProductStock
            {
                ProductId = first.Id,
                CompanyId = tenant.CompanyId,
                EstablishmentId = otherEstablishment.Id,
                Quantity = 99m,
                UpdatedAt = CreatedAt
            });

            var noStock = new Product
            {
                CompanyId = tenant.CompanyId,
                CategoryId = first.CategoryId,
                Name = "No Stock Search Product",
                Barcode = "ZERO-STOCK",
                InternalCode = "ZERO-INT",
                Price = 5m,
                Cost = 1m,
                MinimumStock = 0m,
                VatCategory = ProductVatCategory.Vat15,
                IsActive = true,
                CreatedAt = CreatedAt
            };
            var inactive = new Product
            {
                CompanyId = tenant.CompanyId,
                CategoryId = first.CategoryId,
                Name = "Inactive Search Product",
                Barcode = "INACTIVE-SKU",
                InternalCode = "INACTIVE-INT",
                Price = 5m,
                Cost = 1m,
                MinimumStock = 0m,
                VatCategory = ProductVatCategory.Vat0,
                IsActive = false,
                CreatedAt = CreatedAt
            };
            context.Products.AddRange(noStock, inactive);

            context.Products.AddRange(Enumerable.Range(1, 105).Select(index => new Product
            {
                CompanyId = tenant.CompanyId,
                CategoryId = first.CategoryId,
                Name = $"Bounded Product {index:000}",
                InternalCode = $"BOUND-{index:000}",
                Price = 1m,
                Cost = 0m,
                MinimumStock = 0m,
                VatCategory = ProductVatCategory.Vat0,
                IsActive = true,
                CreatedAt = CreatedAt
            }));

            await context.SaveChangesAsync();
            noStockProductId = noStock.Id;
        }

        await using var readContext = database.CreateDbContext();
        var service = new PosProductLookupService(
            readContext,
            new FixedOperationalContextAccessor(tenant.OperationalContext));

        var barcode = await service.SearchAsync("SKU-EXACT", 30);
        var byName = await service.SearchAsync("Alpha Search", 30);
        var byInternalCode = await service.SearchAsync("CODE-ALPHA", 30);
        var noStock = await service.SearchAsync("ZERO-STOCK", 30);
        var inactive = await service.SearchAsync("INACTIVE-SKU", 30);
        var bounded = await service.SearchAsync(null, 999);

        var exact = Assert.Single(barcode);
        Assert.Equal(tenant.Products[0].Id, exact.Id);
        Assert.Equal(7m, exact.Stock);
        Assert.DoesNotContain(barcode, item => item.Name == "Foreign Search Product");

        Assert.Equal(tenant.Products[1].Id, Assert.Single(byName).Id);
        Assert.Equal(tenant.Products[1].Id, Assert.Single(byInternalCode).Id);

        var zero = Assert.Single(noStock);
        Assert.Equal(noStockProductId, zero.Id);
        Assert.Equal(0m, zero.Stock);

        Assert.Empty(inactive);
        Assert.Equal(100, bounded.Count);
        Assert.DoesNotContain(bounded, item => item.Name == "Foreign Search Product");
        Assert.All(bounded, item => Assert.True(item.IsActive));
    }

    [Fact]
    public async Task Lookup_prioritizes_exact_identifier_then_name_prefix_with_stable_order()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "pos-order", 233, 1m, 2m, 3m);

        await using (var context = database.CreateDbContext())
        {
            var products = await context.Products
                .Where(product => product.CompanyId == tenant.CompanyId)
                .OrderBy(product => product.Id)
                .ToListAsync();

            products[0].Name = "Zulu SKU product";
            products[0].Barcode = "SKU";
            products[1].Name = "SKU Alpha";
            products[1].InternalCode = "OTHER-1";
            products[2].Name = "SKU Beta";
            products[2].InternalCode = "OTHER-2";
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateDbContext();
        var service = new PosProductLookupService(
            readContext,
            new FixedOperationalContextAccessor(tenant.OperationalContext));

        var result = await service.SearchAsync("sku", 30);

        Assert.Equal(tenant.Products[0].Id, result[0].Id);
        Assert.Equal(new[] { "SKU Alpha", "SKU Beta" }, result.Skip(1).Take(2).Select(item => item.Name));
    }

    private sealed class FixedOperationalContextAccessor(OperationalContext context) : IOperationalContextAccessor
    {
        public Task<OperationalContext> GetRequiredContextAsync() => Task.FromResult(context);
    }
}
