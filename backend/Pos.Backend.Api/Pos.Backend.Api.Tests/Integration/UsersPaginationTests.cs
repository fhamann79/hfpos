using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class UsersPaginationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pages_are_bounded_stably_ordered_and_counted_in_SQL()
    {
        var tenant = await SeedAsync(205);
        var commands = new CommandCountingInterceptor();
        await using var context = database.CreateDbContext(commands);
        var controller = Controller(context, tenant);
        var first = await PageAsync(controller.Get());
        Assert.Equal(2, commands.ReaderCount);
        var second = await PageAsync(controller.Get(page: 2));
        Assert.Equal(206, first.TotalItems);
        Assert.Equal(7, first.TotalPages);
        Assert.Equal(1, first.Page);
        Assert.Equal(30, first.PageSize);
        Assert.Equal(30, first.Items.Count);
        Assert.Equal(30, second.Items.Count);
        Assert.Empty(first.Items.Select(u => u.Id).Intersect(second.Items.Select(u => u.Id)));
        Assert.Equal(Enumerable.Range(0, 60).Select(i => $"user-{i:000}"),
            first.Items.Concat(second.Items).Select(u => u.Username));
        var max = await PageAsync(controller.Get(pageSize: 999));
        Assert.Equal(200, max.PageSize);
        Assert.Equal(200, max.Items.Count);
        var min = await PageAsync(controller.Get(page: 0, pageSize: 0));
        Assert.Equal(1, min.Page);
        Assert.Equal(1, min.PageSize);
        Assert.Single(min.Items);
        var overflow = await PageAsync(controller.Get(page: int.MaxValue, pageSize: 200));
        Assert.Empty(overflow.Items);
        Assert.Equal(206, overflow.TotalItems);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("  USER-001  ")]
    [InlineData("  MAIL-001@HFPOS.TEST  ")]
    [InlineData("mail-001")]
    public async Task Search_matches_username_and_email_case_insensitively(string search)
    {
        var tenant = await SeedAsync();
        await using var context = database.CreateDbContext();
        var page = await PageAsync(Controller(context, tenant).Get(search: search));
        Assert.Equal(1, page.TotalItems);
        Assert.Equal("user-001", Assert.Single(page.Items).Username);
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public async Task Status_is_filtered_before_count(bool active, int expected)
    {
        var tenant = await SeedAsync();
        await using var context = database.CreateDbContext();
        var page = await PageAsync(Controller(context, tenant).Get(isActive: active, pageSize: 1));
        Assert.Equal(expected, page.TotalItems);
        Assert.All(page.Items, user => Assert.Equal(active, user.IsActive));
    }

    [Fact]
    public async Task Role_and_combined_filters_are_tenant_scoped_and_do_not_reveal_foreign_roles()
    {
        var tenant = await SeedAsync();
        var foreign = await TestDataBuilder.CreateTenantAsync(database, "foreign-users", 524, 0m);
        await using var context = database.CreateDbContext();
        var ownRoleId = await context.Users.Where(u => u.Id == tenant.UserId).Select(u => u.RoleId).SingleAsync();
        var foreignRoleId = await context.Users.Where(u => u.Id == foreign.UserId).Select(u => u.RoleId).SingleAsync();
        var controller = Controller(context, tenant);
        var own = await PageAsync(controller.Get(roleId: ownRoleId, search: "user-", isActive: true));
        Assert.Equal(2, own.TotalItems);
        Assert.All(own.Items, user => Assert.Equal(ownRoleId, user.RoleId));
        foreach (var roleId in new[] { foreignRoleId, int.MaxValue })
        {
            var empty = await PageAsync(controller.Get(roleId: roleId));
            Assert.Empty(empty.Items);
            Assert.Equal(0, empty.TotalItems);
            Assert.Equal(0, empty.TotalPages);
        }
        var all = await PageAsync(controller.Get());
        Assert.Equal(5, all.TotalItems);
        Assert.All(all.Items, user => Assert.Equal(tenant.CompanyId, user.CompanyId));
        Assert.Empty((await PageAsync(controller.Get(search: "foreign-users"))).Items);
    }

    [Fact]
    public async Task Response_has_only_functional_fields_and_GetById_is_unchanged()
    {
        var tenant = await SeedAsync();
        var foreign = await TestDataBuilder.CreateTenantAsync(database, "foreign-detail", 525, 0m);
        await using var context = database.CreateDbContext();
        var controller = Controller(context, tenant);
        var page = await PageAsync(controller.Get());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page.Items[0]));
        Assert.Equal(new[] { "CompanyId", "Email", "EmissionPointId", "EstablishmentId", "Id", "IsActive", "RoleCode", "RoleId", "RoleName", "Username" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        var detail = Assert.IsType<UserDetailDto>(Assert.IsType<OkObjectResult>((await controller.GetById(tenant.UserId)).Result).Value);
        Assert.Equal(tenant.UserId, detail.Id);
        Assert.Equal(tenant.CompanyId, detail.CompanyId);
        Assert.IsType<NotFoundObjectResult>((await controller.GetById(foreign.UserId)).Result);
    }

    [Fact]
    public async Task Company_users_from_another_establishment_remain_visible()
    {
        var tenant = await SeedAsync();
        await using var context = database.CreateDbContext();
        var establishment = new Establishment
        {
            CompanyId = tenant.CompanyId, Code = "002", Name = "Synthetic second establishment",
            Address = "Test address", IsActive = true, CreatedAt = DateTime.UtcNow
        };
        context.Establishments.Add(establishment);
        await context.SaveChangesAsync();
        var user = await context.Users.SingleAsync(u => u.Username == "user-001");
        user.EstablishmentId = establishment.Id;
        await context.SaveChangesAsync();
        var page = await PageAsync(Controller(context, tenant).Get(search: "user-001"));
        Assert.Equal(establishment.Id, Assert.Single(page.Items).EstablishmentId);
    }

    private async Task<TestTenant> SeedAsync(int count = 4)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "users-page", 523, 0m);
        await using var context = database.CreateDbContext();
        var existing = await context.Users.SingleAsync(u => u.Id == tenant.UserId);
        existing.Username = "zz-seeded-user";
        for (var i = count - 1; i >= 0; i--)
            context.Users.Add(new User
            {
                Username = $"user-{i:000}", Email = $"mail-{i:000}@hfpos.test",
                PasswordHash = "synthetic-test-only-hash", SessionVersion = 123,
                IsActive = i % 2 == 0, CreatedAt = DateTime.UtcNow, CompanyId = tenant.CompanyId,
                RoleId = existing.RoleId, EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = tenant.EmissionPointId
            });
        await context.SaveChangesAsync();
        return tenant;
    }

    private static UsersController Controller(Pos.Backend.Api.Infrastructure.Data.PosDbContext context, TestTenant tenant)
        => new(context, new StaticOperationalContextAccessor(tenant.OperationalContext), new TenantAdministrationGuard(context));

    private static async Task<PagedResultDto<UserListDto>> PageAsync(Task<ActionResult<PagedResultDto<UserListDto>>> task)
        => Assert.IsType<PagedResultDto<UserListDto>>(Assert.IsType<OkObjectResult>((await task).Result).Value);
}
