using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Filters;
using Pos.Backend.Api.WebApi.Middleware;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PlatformControlPlaneTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private const string Password = "synthetic-platform-password-518";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Bootstrap_disabled_enabled_repeated_and_concurrent_never_resets_existing_account()
    {
        await using var one = database.CreateDbContext();
        await PlatformBootstrap.RunAsync(one, new PlatformBootstrapOptions());
        Assert.Empty(await one.PlatformUsers.ToListAsync());
        await using var two = database.CreateDbContext();
        await Task.WhenAll(PlatformBootstrap.RunAsync(one, Bootstrap()), PlatformBootstrap.RunAsync(two, Bootstrap())).WaitAsync(Timeout);
        var original = await one.PlatformUsers.AsNoTracking().SingleAsync();
        await PlatformBootstrap.RunAsync(one, new PlatformBootstrapOptions { Enabled = true });
        var after = await one.PlatformUsers.AsNoTracking().SingleAsync();
        Assert.Equal(original.PasswordHash, after.PasswordHash);
        Assert.Equal(original.Username, after.Username);
        Assert.Equal(1, after.SessionVersion);
    }

    [Theory]
    [InlineData("", "platform@test.local", "synthetic-password")]
    [InlineData("platform", "invalid-email", "synthetic-password")]
    [InlineData("platform", "platform@test.local", "short")]
    public async Task Bootstrap_invalid_configuration_creates_nothing(string username, string email, string password)
    {
        await using var context = database.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformBootstrap.RunAsync(context,
            new PlatformBootstrapOptions { Enabled = true, Username = username, Email = email, Password = password }));
        Assert.Empty(await context.PlatformUsers.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Provision_from_migrations_without_demo_seed_builds_complete_graph_and_initial_admin_login()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        Assert.Empty(await context.Permissions.ToListAsync());
        var request = Request("graph", 301);
        var result = await Service(context, actor).ProvisionAsync(request);
        Assert.False(result.WasAlreadyProcessed);
        Assert.Equal(1, result.Tenant.EstablishmentCount);
        Assert.Equal(1, result.Tenant.EmissionPointCount);
        Assert.Equal(1, result.Tenant.ActiveUserCount);
        Assert.True(result.Tenant.Company.IsActive);
        Assert.Equal("001", (await context.Establishments.SingleAsync()).Code);
        Assert.Equal("N/A", (await context.Establishments.SingleAsync()).Address);
        Assert.Equal("001", (await context.EmissionPoints.SingleAsync()).Code);
        var roles = await context.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission).ToListAsync();
        Assert.Equal(3, roles.Count);
        foreach (var role in roles)
            Assert.Equal(TenantDefaults.RolePermissions[role.Code].Order(), role.RolePermissions.Select(rp => rp.Permission.Code).Order());
        var sri = await context.CompanySriSettings.SingleAsync();
        Assert.False(sri.IsEnabled); Assert.False(sri.CertificateConfigured);
        Assert.Equal(1, sri.Environment); Assert.Equal(1, sri.EmissionType);
        Assert.Empty(await context.Products.ToListAsync()); Assert.Empty(await context.DocumentSequences.ToListAsync());
        Assert.Empty(await context.CompanyEmailSettings.ToListAsync()); Assert.Empty(await context.CashSessions.ToListAsync());
        var audit = await context.PlatformTenantEvents.SingleAsync();
        Assert.DoesNotContain(Password, audit.ProvisioningSnapshot!);
        Assert.DoesNotContain("password-518", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        using var factory = new PlatformApiFactory(database);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, "/api/Auth/login", request.InitialAdmin.Username);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task Platform_login_me_session_stale_inactive_and_invalid_credentials_do_not_enumerate()
    {
        var actor = await ActorAsync();
        using var factory = new PlatformApiFactory(database);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, "/api/platform/auth/login", actor.Username);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == PlatformClaims.TokenType && c.Value == "platform");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == AppClaims.CompanyId || c.Type == AppClaims.Permission);
        var me = await SendAsync(client, HttpMethod.Get, "/api/platform/auth/me", token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.DoesNotContain("passwordHash", await me.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        foreach (var username in new[] { actor.Username, "absent-identity" })
            await ErrorAsync(await client.PostAsJsonAsync("/api/platform/auth/login", new { username, password = "wrong-password" }), 401, "INVALID_CREDENTIALS");
        await using var context = database.CreateDbContext();
        await context.PlatformUsers.ExecuteUpdateAsync(u => u.SetProperty(p => p.SessionVersion, 2L));
        await ErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/platform/auth/me", token), 401, "PLATFORM_SESSION_STALE");
        var fresh = await LoginAsync(client, "/api/platform/auth/login", actor.Username);
        await context.PlatformUsers.ExecuteUpdateAsync(u => u.SetProperty(p => p.IsActive, false));
        await ErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/platform/auth/me", fresh), 401, "PLATFORM_USER_INACTIVE_OR_NOT_FOUND");
    }

    [Fact]
    public async Task Platform_and_tenant_tokens_never_cross_planes_and_existing_restrictions_remain()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var service = Service(context, actor);
        var first = await service.ProvisionAsync(Request("first", 302));
        var second = await service.ProvisionAsync(Request("second", 303));
        using var factory = new PlatformApiFactory(database);
        using var client = factory.CreateClient();
        var platform = await LoginAsync(client, "/api/platform/auth/login", actor.Username);
        var tenant = await LoginAsync(client, "/api/Auth/login", "admin-first");
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/platform/tenants", tenant)).StatusCode);
        await ErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", platform), 401, "INVALID_CLAIMS");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/platform/tenants", Request("public", 304))).StatusCode);
        var list = await SendAsync(client, HttpMethod.Get, "/api/platform/tenants", platform);
        Assert.Equal(2, (await list.Content.ReadFromJsonAsync<PagedResultDto<PlatformTenantDto>>())!.TotalItems);
        var companies = await SendAsync(client, HttpMethod.Get, "/api/Companies", tenant);
        using var json = JsonDocument.Parse(await companies.Content.ReadAsStringAsync());
        Assert.Equal(first.Tenant.Company.Id, json.RootElement.EnumerateArray().Single().GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/api/Companies/{second.Tenant.Company.Id}", tenant)).StatusCode);
        await ErrorAsync(await SendAsync(client, HttpMethod.Post, "/api/Companies", tenant), 403, "PLATFORM_OPERATION_REQUIRED");
        await ErrorAsync(await SendAsync(client, HttpMethod.Delete, $"/api/Companies/{first.Tenant.Company.Id}", tenant), 403, "PLATFORM_OPERATION_REQUIRED");
        await ErrorAsync(await client.PostAsJsonAsync("/api/Auth/register", new { username = "new", email = "new@test.local", password = Password }), 400, "PUBLIC_REGISTRATION_NOT_SUPPORTED");
    }

    [Fact]
    public async Task Idempotence_compares_normalized_payload_and_password_without_storing_password()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var service = Service(context, actor);
        var request = Request("replay", 305);
        var first = await service.ProvisionAsync(request);
        var replay = await service.ProvisionAsync(request with { Company = request.Company with { Name = "  Tenant replay  " } });
        Assert.Equal(first.Tenant.Company.Id, replay.Tenant.Company.Id);
        Assert.True(replay.WasAlreadyProcessed);
        foreach (var changed in new[] { request with { Company = request.Company with { Name = "changed" } },
            request with { InitialAdmin = request.InitialAdmin with { Password = "another-synthetic-password" } } })
            Assert.Equal("TENANT_PROVISIONING_REQUEST_CONFLICT", (await Assert.ThrowsAsync<PlatformException>(() => service.ProvisionAsync(changed))).Message);
        Assert.Equal(1, await context.Companies.CountAsync());
        Assert.Equal(1, await context.PlatformTenantEvents.CountAsync());
    }

    [Theory]
    [InlineData("ruc", "TENANT_RUC_ALREADY_EXISTS")]
    [InlineData("username", "USERNAME_ALREADY_EXISTS")]
    [InlineData("email", "EMAIL_ALREADY_EXISTS")]
    public async Task Global_duplicates_roll_back_the_entire_graph(string field, string code)
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var original = Request("original", 306);
        await Service(context, actor).ProvisionAsync(original);
        var duplicate = Request("duplicate", 307);
        duplicate = field switch
        {
            "ruc" => duplicate with { Company = duplicate.Company with { Ruc = original.Company.Ruc } },
            "username" => duplicate with { InitialAdmin = duplicate.InitialAdmin with { Username = original.InitialAdmin.Username } },
            _ => duplicate with { InitialAdmin = duplicate.InitialAdmin with { Email = original.InitialAdmin.Email } }
        };
        Assert.Equal(code, (await Assert.ThrowsAsync<PlatformException>(() => Service(context, actor).ProvisionAsync(duplicate))).Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.Companies.CountAsync()); Assert.Equal(1, await verify.Users.CountAsync());
        Assert.Equal(3, await verify.Roles.CountAsync()); Assert.Equal(1, await verify.Establishments.CountAsync());
    }

    [Fact]
    public async Task Concurrent_identical_provisioning_has_one_graph_and_conflicting_requests_are_safe()
    {
        var actor = await ActorAsync();
        await using var one = database.CreateDbContext();
        await using var two = database.CreateDbContext();
        var request = Request("concurrent", 308);
        var results = await Task.WhenAll(Service(one, actor).ProvisionAsync(request), Service(two, actor).ProvisionAsync(request)).WaitAsync(Timeout);
        Assert.Equal(results[0].Tenant.Company.Id, results[1].Tenant.Company.Id);
        Assert.Single(results.Where(r => r.WasAlreadyProcessed));
        var conflict = await Assert.ThrowsAsync<PlatformException>(() => Service(two, actor).ProvisionAsync(request with { RequestId = Guid.NewGuid() }));
        Assert.Equal("TENANT_RUC_ALREADY_EXISTS", conflict.Message);
        Assert.Equal(1, await one.Companies.CountAsync()); Assert.Equal(3, await one.Roles.CountAsync());
    }

    [Fact]
    public async Task Synthetic_failure_after_company_insertion_rolls_back_everything()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext(new FailProvisioning());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(context, actor).ProvisionAsync(Request("rollback", 309)));
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.Companies.ToListAsync()); Assert.Empty(await verify.Roles.ToListAsync());
        Assert.Empty(await verify.Users.ToListAsync()); Assert.Empty(await verify.Establishments.ToListAsync());
        Assert.Empty(await verify.Permissions.ToListAsync()); Assert.Empty(await verify.PlatformTenantEvents.ToListAsync());
    }

    [Fact]
    public async Task Listing_filter_count_pagination_and_legacy_detail_are_server_side()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var service = Service(context, actor);
        var one = await service.ProvisionAsync(Request("directory-one", 310));
        var two = await service.ProvisionAsync(Request("directory-two", 311));
        await service.SetActiveAsync(two.Tenant.Company.Id, false, new("Synthetic suspension"));
        var filtered = await service.GetAsync(new() { Search = "directory", Status = "Suspended", PageSize = 1 });
        Assert.Equal(1, filtered.TotalItems); Assert.Equal(two.Tenant.Company.Id, Assert.Single(filtered.Items).Id);
        var second = await service.GetAsync(new() { Page = 2, PageSize = 1 });
        Assert.Equal(2, second.TotalItems); Assert.Single(second.Items);
        var byRuc = await service.GetAsync(new() { Search = one.Tenant.Company.Ruc, PageSize = 1000 });
        Assert.Equal(200, byRuc.PageSize); Assert.Single(byRuc.Items);
        var legacy = await TestDataBuilder.CreateTenantAsync(database, "legacy-platform", 312, 0m);
        var detail = await service.GetByIdAsync(legacy.CompanyId);
        Assert.Empty(detail.Events); Assert.Equal(1, detail.UserCount); Assert.Equal(1, detail.EstablishmentCount);
    }

    [Fact]
    public async Task Suspend_revokes_every_session_and_reactivate_never_revives_old_jwt()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var request = Request("lifecycle", 313);
        var service = Service(context, actor);
        var result = await service.ProvisionAsync(request);
        using var factory = new PlatformApiFactory(database);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, "/api/Auth/login", request.InitialAdmin.Username);
        var suspended = await service.SetActiveAsync(result.Tenant.Company.Id, false, new("Synthetic reason"));
        Assert.False(suspended.Company.IsActive);
        Assert.Equal(2, (await context.Users.AsNoTracking().SingleAsync()).SessionVersion);
        await ErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", token), 401, "SESSION_STALE");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Auth/login", new { username = request.InitialAdmin.Username, password = Password })).StatusCode);
        await service.SetActiveAsync(result.Tenant.Company.Id, false, new(null));
        var activated = await service.SetActiveAsync(result.Tenant.Company.Id, true, new("Synthetic reactivation"));
        Assert.True(activated.Company.IsActive);
        await service.SetActiveAsync(result.Tenant.Company.Id, true, new(null));
        await ErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", token), 401, "SESSION_STALE");
        var fresh = await LoginAsync(client, "/api/Auth/login", request.InitialAdmin.Username);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", fresh)).StatusCode);
        Assert.Equal(3, await context.PlatformTenantEvents.CountAsync());
        Assert.True(await context.Establishments.AllAsync(e => e.IsActive));
        Assert.True(await context.EmissionPoints.AllAsync(e => e.IsActive));
        Assert.True(await context.Users.AllAsync(u => u.IsActive));
    }

    [Theory]
    [InlineData("ruc", "TENANT_RUC_ALREADY_EXISTS")]
    [InlineData("username", "USERNAME_ALREADY_EXISTS")]
    [InlineData("email", "EMAIL_ALREADY_EXISTS")]
    [InlineData("request", "TENANT_PROVISIONING_REQUEST_CONFLICT")]
    public async Task Concurrent_conflicting_requests_commit_only_one_graph(string field, string code)
    {
        var actor = await ActorAsync();
        await using var one = database.CreateDbContext();
        await using var two = database.CreateDbContext();
        var first = Request("race-one", 320);
        var second = Request("race-two", 321);
        second = field switch
        {
            "ruc" => second with { Company = second.Company with { Ruc = first.Company.Ruc } },
            "username" => second with { InitialAdmin = second.InitialAdmin with { Username = first.InitialAdmin.Username } },
            "email" => second with { InitialAdmin = second.InitialAdmin with { Email = first.InitialAdmin.Email } },
            _ => second with { RequestId = first.RequestId }
        };
        async Task<PlatformException?> Attempt(PosDbContext db, TenantProvisionRequest request)
        {
            try { await Service(db, actor).ProvisionAsync(request); return null; }
            catch (PlatformException exception) { return exception; }
        }
        var results = await Task.WhenAll(Attempt(one, first), Attempt(two, second)).WaitAsync(Timeout);
        Assert.Single(results.Where(r => r is null));
        Assert.Equal(code, Assert.Single(results.Where(r => r is not null))!.Message);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.Companies.CountAsync()); Assert.Equal(1, await verify.Users.CountAsync());
        Assert.Equal(3, await verify.Roles.CountAsync()); Assert.Equal(1, await verify.PlatformTenantEvents.CountAsync());
    }

    [Fact]
    public async Task Suspension_waits_for_existing_operational_writer_to_commit()
    {
        var actor = await ActorAsync();
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "writer-first", 322, 5m);
        await using (var setup = new TestServiceScope(database, tenant.OperationalContext))
            await setup.CashSessions.OpenAsync(new OpenCashSessionDto { OpeningAmount = 20m });
        var gate = new UncommittedSaveGate(db => db.ChangeTracker.Entries<Sale>().Any(e => e.State == EntityState.Added));
        await using var writer = new TestServiceScope(database, tenant.OperationalContext, gate);
        await using var lifecycle = database.CreateDbContext();
        await writer.DbContext.Database.OpenConnectionAsync();
        await lifecycle.Database.OpenConnectionAsync();
        var sale = writer.Sales.CreateAsync(SaleRequest(tenant));
        await gate.Saved.WaitAsync();
        var suspend = Service(lifecycle, actor).SetActiveAsync(tenant.CompanyId, false, new("Synthetic concurrent suspension"));
        try { await AssertBlockedAsync(suspend, lifecycle, writer.DbContext); }
        finally { gate.Release.Set(); }
        await sale.WaitAsync(Timeout);
        await suspend.WaitAsync(Timeout);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Sales.ToListAsync());
        Assert.Equal(4m, await TestDataBuilder.GetStockAsync(verify, tenant, tenant.Products[0].Id));
        Assert.False((await verify.Companies.SingleAsync()).IsActive);
        Assert.Equal(2, (await verify.Users.SingleAsync()).SessionVersion);
    }

    [Fact]
    public async Task Writer_waiting_behind_suspension_revalidates_and_cannot_write()
    {
        var actor = await ActorAsync();
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "suspend-first", 323, 5m);
        await using (var setup = new TestServiceScope(database, tenant.OperationalContext))
            await setup.CashSessions.OpenAsync(new OpenCashSessionDto { OpeningAmount = 20m });
        var gate = new UncommittedSaveGate(db => db.ChangeTracker.Entries<PlatformTenantEvent>()
            .Any(e => e.State == EntityState.Added && e.Entity.EventType == PlatformTenantEventType.Suspended));
        await using var lifecycle = database.CreateDbContext(gate);
        await using var writer = new TestServiceScope(database, tenant.OperationalContext);
        await lifecycle.Database.OpenConnectionAsync();
        await writer.DbContext.Database.OpenConnectionAsync();
        var suspend = Service(lifecycle, actor).SetActiveAsync(tenant.CompanyId, false, new("Synthetic barrier"));
        await gate.Saved.WaitAsync();
        var sale = writer.Sales.CreateAsync(SaleRequest(tenant));
        try { await AssertBlockedAsync(sale, writer.DbContext, lifecycle); }
        finally { gate.Release.Set(); }
        await suspend.WaitAsync(Timeout);
        Assert.Equal("CONTEXT_MISMATCH", (await Assert.ThrowsAsync<OperationalContextException>(() => sale.WaitAsync(Timeout))).ErrorCode);
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.Sales.ToListAsync()); Assert.Empty(await verify.InventoryMovements.ToListAsync());
        Assert.Equal(5m, await TestDataBuilder.GetStockAsync(verify, tenant, tenant.Products[0].Id));
        Assert.False((await verify.Companies.SingleAsync()).IsActive);
    }

    private static SaleCreateDto SaleRequest(TestTenant tenant) => new()
    {
        DocumentType = SaleDocumentType.Ticket, PaymentMethod = SalePaymentMethod.Cash,
        Items = [new SaleItemCreateDto { ProductId = tenant.Products[0].Id, Quantity = 1m, UnitPrice = tenant.Products[0].Price }]
    };

    private async Task AssertBlockedAsync(Task operation, PosDbContext waiter, PosDbContext blocker)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand("SELECT pg_blocking_pids(@waiter)", observer);
        command.Parameters.AddWithValue("waiter", ((NpgsqlConnection)waiter.Database.GetDbConnection()).ProcessID);
        var blockerId = ((NpgsqlConnection)blocker.Database.GetDbConnection()).ProcessID;
        while (true)
        {
            var blockers = (int[])(await command.ExecuteScalarAsync(timeout.Token))!;
            if (blockers.Contains(blockerId)) { Assert.False(operation.IsCompleted); return; }
            Assert.False(operation.IsCompleted, "Operation did not wait for the Company barrier.");
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class UncommittedSaveGate(Func<DbContext, bool> matches) : SaveChangesInterceptor
    {
        public AsyncTestSignal Saved { get; } = new();
        public AsyncTestSignal Release { get; } = new();
        private bool pause;
        private bool used;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            pause = !used && data.Context is not null && matches(data.Context);
            if (pause) used = true;
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data,
            int result, CancellationToken cancellationToken = default)
        {
            if (pause) { Saved.Set(); await Release.WaitAsync(cancellationToken); }
            return result;
        }
    }

    [Fact]
    public async Task Missing_platform_metadata_fails_closed_and_mixed_claims_are_rejected()
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actor.UserId.ToString()),
            new(AppClaims.Username, actor.Username), new(ClaimTypes.Role, PlatformClaims.AdminRole),
            new(PlatformClaims.TokenType, "platform"), new(PlatformClaims.SessionVersion, "1") };
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "synthetic")) };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var platform = new PlatformContextAccessor(accessor, context);
        var tenant = new OperationalContextAccessor(accessor, context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OperationalContextAccessor>.Instance);
        var invoked = false;
        var middleware = new OperationalSessionMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute(AppPolicies.PlatformAdmin)), "forgotten-platform-marker"));
        Assert.Equal("INVALID_CLAIMS", (await Assert.ThrowsAsync<OperationalContextException>(() => middleware.InvokeAsync(http, tenant, platform))).ErrorCode);
        Assert.False(invoked);
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
            new AuthorizeAttribute(AppPolicies.PlatformAdmin), new RequirePlatformContextAttribute()), "explicit-platform-marker"));
        await middleware.InvokeAsync(http, tenant, platform);
        Assert.True(invoked);
        var mixed = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            claims.Append(new Claim(AppClaims.CompanyId, "1")), "synthetic")) };
        accessor.HttpContext = mixed;
        Assert.Equal("INVALID_PLATFORM_CLAIMS", (await Assert.ThrowsAsync<PlatformException>(() => platform.GetRequiredContextAsync())).Message);
    }

    [Theory]
    [InlineData("name", "TENANT_PROVISIONING_INVALID")]
    [InlineData("ruc", "INVALID_COMPANY_RUC")]
    [InlineData("timezone", "COMPANY_TIMEZONE_INVALID")]
    [InlineData("email", "TENANT_ADMIN_INVALID")]
    [InlineData("password", "TENANT_ADMIN_INVALID")]
    [InlineData("request", "TENANT_PROVISIONING_INVALID")]
    public async Task Invalid_provisioning_never_creates_a_partial_tenant(string field, string code)
    {
        var actor = await ActorAsync();
        await using var context = database.CreateDbContext();
        var request = Request("validation", 340);
        request = field switch
        {
            "name" => request with { Company = request.Company with { Name = " " } },
            "ruc" => request with { Company = request.Company with { Ruc = "123" } },
            "timezone" => request with { Company = request.Company with { TimeZoneId = "Invalid/Synthetic" } },
            "email" => request with { InitialAdmin = request.InitialAdmin with { Email = "invalid-email" } },
            "password" => request with { InitialAdmin = request.InitialAdmin with { Password = "short" } },
            _ => request with { RequestId = Guid.Empty }
        };
        var exception = await Record.ExceptionAsync(() => Service(context, actor).ProvisionAsync(request));
        Assert.NotNull(exception);
        var actualCode = exception is OperationalContextException operational ? operational.ErrorCode : exception.Message;
        Assert.Equal(code, actualCode);
        Assert.Empty(await context.Companies.ToListAsync()); Assert.Empty(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task Suspension_requires_reason_and_revokes_inactive_users_without_changing_assignments()
    {
        var actor = await ActorAsync();
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "all-sessions", 341, 1m);
        await using var context = database.CreateDbContext();
        var user = await context.Users.SingleAsync();
        context.Users.Add(new User { CompanyId = user.CompanyId, EstablishmentId = user.EstablishmentId,
            EmissionPointId = user.EmissionPointId, RoleId = user.RoleId, Username = "inactive-synthetic",
            Email = "inactive@test.local", PasswordHash = "synthetic-only", IsActive = false, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        var service = Service(context, actor);
        Assert.Equal("TENANT_REASON_REQUIRED", (await Assert.ThrowsAsync<PlatformException>(() => service.SetActiveAsync(tenant.CompanyId, false, new(" ")))).Message);
        Assert.True(await context.Companies.AllAsync(c => c.IsActive));
        Assert.Empty(await context.PlatformTenantEvents.ToListAsync());
        await service.SetActiveAsync(tenant.CompanyId, false, new("Synthetic required reason"));
        Assert.True(await context.Users.AllAsync(u => u.SessionVersion == 2));
        Assert.Equal(1, await context.Users.CountAsync(u => u.IsActive));
        Assert.True(await context.Users.AllAsync(u => u.RoleId == user.RoleId && u.CompanyId == tenant.CompanyId));
    }

    private async Task<PlatformContext> ActorAsync()
    {
        await using var context = database.CreateDbContext();
        await PlatformBootstrap.RunAsync(context, Bootstrap());
        var actor = await context.PlatformUsers.SingleAsync();
        return new(actor.Id, actor.Username, actor.Email, actor.SessionVersion);
    }
    private static PlatformBootstrapOptions Bootstrap() => new() { Enabled = true, Username = "platform-test", Email = "platform@test.local", Password = Password };
    private PlatformTenantService Service(PosDbContext context, PlatformContext actor) => new(context,
        new StaticPlatformAccessor(actor), new FixedBusinessClock(), new TenantAdministrationGuard(context));
    private static TenantProvisionRequest Request(string key, int seed) => new(Guid.NewGuid(),
        new("Tenant " + key, $"9{seed:D9}001", "America/Guayaquil"), new("Matriz", null), new("Caja Principal"),
        new("admin-" + key, key + "@test.local", Password));
    private static async Task<string> LoginAsync(HttpClient client, string url, string username)
    {
        var response = await client.PostAsJsonAsync(url, new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
    private static async Task ErrorAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
    }
    private sealed class StaticPlatformAccessor(PlatformContext actor) : IPlatformContextAccessor
    { public Task<PlatformContext> GetRequiredContextAsync() => Task.FromResult(actor); }
    private sealed class FailProvisioning : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<PlatformTenantEvent>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic provisioning failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class PlatformApiFactory(PostgresDatabaseFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString, ["SeedDemoData"] = "false", ["PlatformBootstrap:Enabled"] = "false",
                ["Jwt:Key"] = "synthetic-platform-http-test-key-long-enough-518", ["Jwt:Issuer"] = "hfpos-platform-test", ["Jwt:Audience"] = "hfpos-platform-test"
            }));
        }
    }
}
