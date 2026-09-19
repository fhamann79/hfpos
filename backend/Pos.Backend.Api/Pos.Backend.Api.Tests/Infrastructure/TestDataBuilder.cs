using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed record TestProduct(int Id, decimal InitialStock, decimal Price);

internal sealed record TestTenant(
    OperationalContext OperationalContext,
    int CompanyId,
    int EstablishmentId,
    int EmissionPointId,
    int UserId,
    IReadOnlyList<TestProduct> Products);

internal static class TestDataBuilder
{
    private static readonly DateTime CreatedAt = new(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc);

    public static async Task<TestTenant> CreateTenantAsync(
        PostgresDatabaseFixture database,
        string key,
        int numericSeed,
        params decimal[] productStocks)
    {
        if (productStocks.Length == 0)
        {
            throw new ArgumentException("At least one product is required.", nameof(productStocks));
        }

        await using var context = database.CreateDbContext();

        var company = new Company
        {
            Name = $"Test company {key}",
            Ruc = $"179{numericSeed:000000000}1",
            TimeZoneId = "America/Guayaquil",
            CreatedAt = CreatedAt,
            IsActive = true
        };
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var establishment = new Establishment
        {
            CompanyId = company.Id,
            Code = "001",
            Name = $"Test establishment {key}",
            Address = "Test address",
            IsActive = true,
            CreatedAt = CreatedAt
        };
        var role = new Role
        {
            CompanyId = company.Id,
            Code = AppRoles.Cashier,
            Name = "Test cashier",
            IsActive = true,
            CreatedAt = CreatedAt
        };
        var category = new Category
        {
            CompanyId = company.Id,
            Name = $"Test category {key}",
            IsActive = true,
            CreatedAt = CreatedAt
        };
        context.AddRange(establishment, role, category);
        await context.SaveChangesAsync();

        var emissionPoint = new EmissionPoint
        {
            EstablishmentId = establishment.Id,
            Code = "001",
            Name = $"Test emission point {key}",
            IsActive = true,
            CreatedAt = CreatedAt
        };
        context.EmissionPoints.Add(emissionPoint);
        await context.SaveChangesAsync();

        var user = new User
        {
            Username = $"cashier-{key}",
            Email = $"cashier-{key}@hfpos.test",
            PasswordHash = "test-only-password-hash",
            IsActive = true,
            CreatedAt = CreatedAt,
            RoleId = role.Id,
            CompanyId = company.Id,
            EstablishmentId = establishment.Id,
            EmissionPointId = emissionPoint.Id
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var products = productStocks.Select((stock, index) => new Product
        {
            CompanyId = company.Id,
            CategoryId = category.Id,
            Name = $"Test product {key}-{index + 1}",
            InternalCode = $"{key}-P{index + 1}",
            Price = 10m,
            Cost = 2m,
            MinimumStock = 3m,
            VatCategory = ProductVatCategory.Vat0,
            IsActive = true,
            CreatedAt = CreatedAt
        }).ToList();

        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        var stocks = products.Select((product, index) => new ProductStock
        {
            ProductId = product.Id,
            CompanyId = company.Id,
            EstablishmentId = establishment.Id,
            Quantity = productStocks[index],
            UpdatedAt = CreatedAt
        }).ToList();
        context.ProductStocks.AddRange(stocks);
        await context.SaveChangesAsync();

        var operationalContext = new OperationalContext
        {
            CompanyId = company.Id,
            EstablishmentId = establishment.Id,
            EmissionPointId = emissionPoint.Id,
            CompanyTimeZoneId = company.TimeZoneId,
            Username = user.Username,
            UserId = user.Id
        };

        return new TestTenant(
            operationalContext,
            company.Id,
            establishment.Id,
            emissionPoint.Id,
            user.Id,
            products.Select((product, index) => new TestProduct(
                product.Id,
                productStocks[index],
                product.Price)).ToList());
    }

    public static async Task<decimal> GetStockAsync(
        PosDbContext context,
        TestTenant tenant,
        int productId)
        => await context.ProductStocks
            .Where(stock => stock.CompanyId == tenant.CompanyId
                && stock.EstablishmentId == tenant.EstablishmentId
                && stock.ProductId == productId)
            .Select(stock => stock.Quantity)
            .SingleAsync();
}
