using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PosQuickProductsTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private const string TestPassword = "quick-products-test-only-password";
    private static readonly CancellationToken Ct = CancellationToken.None;
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Favorites_are_ordered_shared_within_company_and_reject_foreign_or_duplicate_ids()
    {
        var a = await TestDataBuilder.CreateTenantAsync(database, "fav-a", 428, 0, 2);
        var b = await TestDataBuilder.CreateTenantAsync(database, "fav-b", 429, 3);
        await using var db = database.CreateDbContext();
        var service = Service(db, a.OperationalContext);
        var ids = a.Products.Select(p => p.Id).Reverse().ToArray();
        Assert.Equal(0, (await service.GetFavoritesAsync(Ct)).Revision);
        await Save(service, 0, ids);
        var anotherPoint = new OperationalContext { CompanyId = a.CompanyId, EmissionPointId = a.EmissionPointId + 1 };
        Assert.Equal(ids, (await Service(db, anotherPoint).GetFavoritesAsync(Ct)).ProductIds);
        Assert.Empty((await Service(db, b.OperationalContext).GetFavoritesAsync(Ct)).ProductIds);
        await AssertError("POS_FAVORITES_PRODUCT_INVALID", () => Save(service, 1, [b.Products[0].Id]));
        await AssertError("POS_FAVORITES_INVALID", () => Save(service, 1, [ids[0], ids[0]]));
        await AssertError("POS_FAVORITES_INVALID", () => Save(service, 1, Enumerable.Range(1, 13).ToArray()));
        await AssertError("POS_FAVORITES_STALE", () => Save(service, 0, []));
        Assert.Equal(ids.Reverse(), (await Save(service, 1, ids.Reverse().ToArray())).ProductIds);
        (await db.Products.SingleAsync(p => p.Id == ids[0])).IsActive = false;
        await db.SaveChangesAsync();
        var removed = await Save(service, 2, [ids[1]]);
        Assert.Equal(new[] { ids[1] }, removed.ProductIds);
        await AssertError("POS_FAVORITES_PRODUCT_INVALID", () => Save(service, 3, ids));
        Assert.Empty((await Save(service, 3, [])).ProductIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Only_one_concurrent_writer_can_save_the_same_revision(int revision)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "fav-race", 430, 2, 2);
        if (revision == 1)
        {
            await using var seed = database.CreateDbContext();
            await Save(Service(seed, tenant.OperationalContext), 0, []);
        }
        var barrier = new FavoritesWriteBarrier();
        await using var first = database.CreateDbContext(barrier);
        await using var second = database.CreateDbContext(barrier);
        var a = Service(first, tenant.OperationalContext);
        var b = Service(second, tenant.OperationalContext);
        async Task<PosFavoritesDto?> Attempt(PosQuickProductService service, int id)
        {
            try { return await Save(service, revision, [id]); }
            catch (InvalidOperationException ex) when (ex.Message == "POS_FAVORITES_STALE") { return null; }
        }
        var result = await Task.WhenAll(Attempt(a, tenant.Products[0].Id), Attempt(b, tenant.Products[1].Id));
        var winner = Assert.Single(result.OfType<PosFavoritesDto>());
        Assert.Equal(2, barrier.Arrivals);
        Assert.Equal(revision + 1, winner.Revision);
        Assert.Equal(winner.ProductIds, (await a.GetFavoritesAsync(Ct)).ProductIds);
    }

    [Fact]
    public async Task Ranking_uses_net_quantity_effective_notes_and_only_completed_sales_from_the_active_point()
    {
        var a = await TestDataBuilder.CreateTenantAsync(database, "rank-a", 431, Enumerable.Repeat(2m, 16).ToArray());
        var b = await TestDataBuilder.CreateTenantAsync(database, "rank-b", 432, 2);
        await using var db = database.CreateDbContext();
        var now = DateTime.UtcNow.AddMinutes(-1);
        var ids = a.Products.Select(product => product.Id).ToArray();
        var otherPoint = new EmissionPoint { EstablishmentId = a.EstablishmentId, Code = "002",
            Name = "Synthetic other point", IsActive = true, CreatedAt = now };
        db.EmissionPoints.Add(otherPoint);
        (await db.Products.SingleAsync(p => p.Id == ids[3])).IsActive = false;
        await db.SaveChangesAsync();

        var main = SaleFor(a, now, ids.Select((id, i) => new SaleItem { ProductId = id,
            Quantity = i switch { 0 => 20, 1 or 2 => 10, 3 => 900, _ => 1 } }).ToArray());
        var old = SaleFor(a, now.AddDays(-31), [new() { ProductId = ids[0], Quantity = 200 }]);
        var voided = SaleFor(a, now, [new() { ProductId = ids[0], Quantity = 200 }]);
        voided.Status = SaleStatus.Voided;
        voided.VoidedAt = now;
        var other = SaleFor(a, now, [new() { ProductId = ids[0], Quantity = 200 }]);
        other.EmissionPointId = otherPoint.Id;
        db.Sales.AddRange(main, old, voided, other, SaleFor(b, now, [new() { ProductId = b.Products[0].Id, Quantity = 200 }]));
        await db.SaveChangesAsync();

        var authorized = NoteFor(main, ids[0], 12, SaleDocumentStatus.Authorized, now);
        var legacy = NoteFor(main, ids[2], 2, SaleDocumentStatus.PendingAuthorization, now);
        legacy.SriAuthorizationStatus = " autorizado ";
        var rejected = NoteFor(main, ids[1], 10, SaleDocumentStatus.Rejected, now);
        rejected.SriAuthorizationStatus = "AUTORIZADO";
        var missingKey = NoteFor(main, ids[1], 10, SaleDocumentStatus.Authorized, now);
        missingKey.AccessKey = " ";
        var wrongPoint = NoteFor(main, ids[1], 10, SaleDocumentStatus.Authorized, now);
        wrongPoint.EmissionPointId = otherPoint.Id;
        var cancelled = NoteFor(main, ids[1], 10, SaleDocumentStatus.Cancelled, now);
        cancelled.SriAuthorizationStatus = "AUTORIZADO";
        var voidedNote = NoteFor(main, ids[1], 10, SaleDocumentStatus.Authorized, now);
        voidedNote.VoidedAt = now;
        db.CreditNotes.AddRange(authorized, legacy, rejected, missingKey, wrongPoint, cancelled, voidedNote);
        await db.SaveChangesAsync();

        var queries = new QueryCounter();
        await using var read = database.CreateDbContext(queries);
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }.Concat(ids.Skip(4).Take(9)),
            await Service(read, a.OperationalContext).GetTopSellersAsync(Ct));
        var sql = Assert.Single(queries.Commands);
        Assert.Contains("UNION ALL", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Equal(new[] { b.Products[0].Id }, await Service(db, b.OperationalContext).GetTopSellersAsync(Ct));
    }

    [Fact]
    public async Task Http_reads_require_pos_and_lookup_permissions_and_writes_also_require_catalog_write()
    {
        var reader = await TestDataBuilder.CreateTenantAsync(database, "quick-reader", 433, 2);
        var writer = await TestDataBuilder.CreateTenantAsync(database, "quick-writer", 434, 2);
        var denied = await TestDataBuilder.CreateTenantAsync(database, "quick-denied", 435, 2);
        await using (var db = database.CreateDbContext())
        {
            var permissions = new[] { AppPermissions.PosSalesCreate, AppPermissions.InventoryRead,
                AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite }
                .Select(code => new Permission { Code = code, Description = code, IsActive = true, CreatedAt = DateTime.UtcNow })
                .ToArray();
            foreach (var (tenant, grants) in new[] { (reader, permissions[..3]), (writer, permissions), (denied, permissions[1..]) })
            {
                var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId);
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, TestPassword);
                foreach (var permission in grants)
                    db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
            }
            await db.SaveChangesAsync();
        }

        using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();
        const string favorites = "/api/PosQuickProducts/favorites";
        const string ranking = "/api/PosQuickProducts/top-sellers";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(favorites)).StatusCode);
        await Login(client, denied);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ranking)).StatusCode);
        await Login(client, reader);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ranking)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(favorites)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(favorites, new { revision = 0, productIds = Array.Empty<int>() })).StatusCode);
        await Login(client, writer);
        var foreign = await client.PutAsJsonAsync(favorites, new { revision = 0, productIds = new[] { reader.Products[0].Id } });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        var body = new { revision = 0, productIds = new[] { writer.Products[0].Id } };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(favorites, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(favorites, body)).StatusCode);
        await Login(client, reader);
        Assert.Empty((await client.GetFromJsonAsync<PosFavoritesDto>(favorites))!.ProductIds);
    }

    [Fact]
    public async Task Additive_migration_rolls_back_only_favorites_and_preserves_catalog()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "quick-migration", 436, 2);
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await Save(Service(db, tenant.OperationalContext), 0, [tenant.Products[0].Id]);
        try
        {
            await migrator.MigrateAsync("20261021235813_AddNonInventoryServices");
            Assert.False(await db.Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('public."PosFavoriteLists"') IS NOT NULL AS "Value" """).SingleAsync());
            Assert.True(await db.Products.AnyAsync(product => product.Id == tenant.Products[0].Id));
        }
        finally { await migrator.MigrateAsync(); }

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty((await Service(db, tenant.OperationalContext).GetFavoritesAsync(Ct)).ProductIds);
        Assert.True(await db.Products.AnyAsync(product => product.Id == tenant.Products[0].Id));
    }

    private static Sale SaleFor(TestTenant tenant, DateTime now, SaleItem[] items) => new()
    {
        CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId, EmissionPointId = tenant.EmissionPointId,
        UserId = tenant.UserId, CreatedAt = now, Status = SaleStatus.Completed,
        BusinessDate = DateOnly.FromDateTime(now), Items = items
    };

    private static CreditNote NoteFor(Sale sale, int productId, decimal quantity, SaleDocumentStatus status, DateTime now) => new()
    {
        CompanyId = sale.CompanyId, EstablishmentId = sale.EstablishmentId, EmissionPointId = sale.EmissionPointId,
        UserId = sale.UserId, OriginalSaleId = sale.Id, DocumentStatus = status, BusinessDate = sale.BusinessDate,
        Reason = "Synthetic return", CreatedAt = now, AccessKey = Guid.NewGuid().ToString("N"),
        AuthorizationNumber = "synthetic-auth", Items = [new() { ProductId = productId, Quantity = quantity }]
    };

    private static async Task Login(HttpClient client, TestTenant tenant)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/Auth/login", new { username = tenant.OperationalContext.Username, password = TestPassword });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
    }

    private static PosQuickProductService Service(PosDbContext db, OperationalContext context) => new(db, new FixedContext(context));
    private static Task<PosFavoritesDto> Save(PosQuickProductService service, long revision, int[] ids)
        => service.SaveFavoritesAsync(new SavePosFavoritesDto { Revision = revision, ProductIds = ids }, Ct);
    private static async Task AssertError(string code, Func<Task> action)
        => Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(action)).Message);

    private sealed class FixedContext(OperationalContext context) : IOperationalContextAccessor
    {
        public Task<OperationalContext> GetRequiredContextAsync() => Task.FromResult(context);
    }

    private sealed class FavoritesWriteBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => arrivals;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(""" "PosFavoriteLists" """.Trim(), StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2) bothReady.TrySetResult();
                await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ApiFactory(PostgresDatabaseFixture database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["SeedDemoData"] = "false",
                    ["Jwt:Key"] = "quick-products-ephemeral-test-only-signing-key",
                    ["Jwt:Issuer"] = "quick-products-test",
                    ["Jwt:Audience"] = "quick-products-test"
                }));
        }
    }
}
