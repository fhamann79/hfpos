using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class MasterDataPaginationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Customers_page_is_tenant_scoped_filtered_stable_and_bounded()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "customer-page", 211, 0m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "customer-other", 212, 0m);

        await using (var context = database.CreateDbContext())
        {
            context.Customers.AddRange(
                Customer(tenant.CompanyId, "Bravo", "0992", true, 1),
                Customer(tenant.CompanyId, "Alpha", "0991", true, 2),
                Customer(tenant.CompanyId, "Charlie", "0993", false, 3),
                Customer(otherTenant.CompanyId, "Foreign", "0999", true, 4));
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateDbContext();
        var accessor = new FixedOperationalContextAccessor(tenant.OperationalContext);
        var queryService = new CustomerQueryService(readContext, accessor);
        var controller = new CustomersController(readContext, accessor, queryService);

        var first = await PageAsync(controller.GetPage(null, "all", page: 1, pageSize: 2));
        var second = await PageAsync(controller.GetPage(null, "all", page: 2, pageSize: 2));
        var filtered = await PageAsync(controller.GetPage("alp", "all", page: 1, pageSize: 50));
        var minimum = await PageAsync(controller.GetPage(null, "all", page: 0, pageSize: 0));
        var maximum = await PageAsync(controller.GetPage(null, "all", page: 1, pageSize: 999));

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new[] { "Alpha", "Bravo" }, first.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Charlie" }, second.Items.Select(item => item.Name));
        Assert.Single(filtered.Items);
        Assert.Equal("Alpha", filtered.Items[0].Name);
        Assert.Equal(1, minimum.Page);
        Assert.Equal(1, minimum.PageSize);
        Assert.Single(minimum.Items);
        Assert.Equal(200, maximum.PageSize);
        Assert.Equal(3, maximum.Items.Count);
        Assert.DoesNotContain(first.Items.Concat(second.Items), item => item.Name == "Foreign");
    }

    [Fact]
    public async Task Suppliers_page_and_lookup_are_tenant_scoped_filtered_stable_and_bounded()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "supplier-page", 213, 0m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "supplier-other", 214, 0m);

        await using (var context = database.CreateDbContext())
        {
            context.Suppliers.AddRange(
                Supplier(tenant.CompanyId, "Bravo Supplier", "SUP-2", true, 1),
                Supplier(tenant.CompanyId, "Alpha Supplier", "SUP-1", true, 2),
                Supplier(tenant.CompanyId, "Inactive Supplier", "SUP-3", false, 3),
                Supplier(otherTenant.CompanyId, "Foreign Supplier", "SUP-X", true, 4));
            await context.SaveChangesAsync();
        }

        await using var readContext = database.CreateDbContext();
        var accessor = new FixedOperationalContextAccessor(tenant.OperationalContext);
        var queryService = new SupplierQueryService(readContext, accessor);
        var controller = new SuppliersController(readContext, accessor, queryService);

        var first = await SupplierPageAsync(controller.GetPage(null, "all", page: 1, pageSize: 2));
        var second = await SupplierPageAsync(controller.GetPage(null, "all", page: 2, pageSize: 2));
        var filtered = await SupplierPageAsync(controller.GetPage("alpha", "active", page: 1, pageSize: 50));
        var maximum = await SupplierPageAsync(controller.GetPage(null, "all", page: 1, pageSize: 999));

        var lookupAction = await controller.Lookup(null, take: 999);
        var lookupOk = Assert.IsType<OkObjectResult>(lookupAction.Result);
        var lookup = Assert.IsAssignableFrom<IEnumerable<SupplierDto>>(lookupOk.Value).ToArray();

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new[] { "Alpha Supplier", "Bravo Supplier" }, first.Items.Select(item => item.Name));
        Assert.Equal(new[] { "Inactive Supplier" }, second.Items.Select(item => item.Name));
        Assert.Single(filtered.Items);
        Assert.Equal("Alpha Supplier", filtered.Items[0].Name);
        Assert.Equal(200, maximum.PageSize);
        Assert.Equal(3, maximum.Items.Count);
        Assert.Equal(new[] { "Alpha Supplier", "Bravo Supplier" }, lookup.Select(item => item.Name));
        Assert.DoesNotContain(lookup, item => item.Name == "Inactive Supplier" || item.Name == "Foreign Supplier");
    }

    private static Customer Customer(int companyId, string name, string phone, bool active, int minute)
        => new()
        {
            CompanyId = companyId,
            Name = name,
            Phone = phone,
            IsActive = active,
            CreatedAt = new DateTime(2026, 9, 18, 10, minute, 0, DateTimeKind.Utc)
        };

    private static Supplier Supplier(int companyId, string name, string identification, bool active, int minute)
        => new()
        {
            CompanyId = companyId,
            Name = name,
            Identification = identification,
            IsActive = active,
            CreatedAt = new DateTime(2026, 9, 18, 11, minute, 0, DateTimeKind.Utc)
        };

    private static async Task<PagedResultDto<CustomerDto>> PageAsync(Task<ActionResult<PagedResultDto<CustomerDto>>> task)
    {
        var action = await task;
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<PagedResultDto<CustomerDto>>(ok.Value);
    }

    private static async Task<PagedResultDto<SupplierDto>> SupplierPageAsync(Task<ActionResult<PagedResultDto<SupplierDto>>> task)
    {
        var action = await task;
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<PagedResultDto<SupplierDto>>(ok.Value);
    }

    private sealed class FixedOperationalContextAccessor(OperationalContext context) : IOperationalContextAccessor
    {
        public Task<OperationalContext> GetRequiredContextAsync() => Task.FromResult(context);
    }
}
