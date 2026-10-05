using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class AssistedPasswordRecoveryTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    internal const string Legacy = "old-only";
    internal const string NewPassword = "new synthetic password 530";
    internal const string PlatformPassword = "synthetic platform password 530";
    private const string TenantBase = "/api/account/recovery";
    private const string PlatformBase = "/api/platform/account/recovery";
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private ProductionSecurityApiFactory Factory() => new(new() {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
        ["AuthRateLimit:PermitLimit"] = "100" });

    internal static async Task<(TestTenant Tenant, User Owner, int PlatformActor, int PlatformTarget)> SeedAsync(PostgresDatabaseFixture database)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "recovery530", 530, 1m);
        await using var db = database.CreateDbContext();
        var admin = await db.Users.Include(u => u.Role).SingleAsync(u => u.Id == tenant.UserId);
        admin.Role.Code = AppRoles.Admin;
        admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, Legacy);
        var cashier = new Role { CompanyId = tenant.CompanyId, Code = AppRoles.Cashier, Name = "Synthetic cashier", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Roles.Add(cashier); await db.SaveChangesAsync();
        var owner = new User { Username = "owner-530", Email = "owner530@test.invalid", CompanyId = tenant.CompanyId,
            RoleId = cashier.Id, EstablishmentId = tenant.EstablishmentId, EmissionPointId = tenant.EmissionPointId,
            IsActive = true, CreatedAt = DateTime.UtcNow };
        owner.PasswordHash = new PasswordHasher<User>().HashPassword(owner, Legacy); db.Users.Add(owner);
        foreach (var definition in TenantDefaults.Permissions)
        {
            var p = await db.Permissions.SingleOrDefaultAsync(p => p.Code == definition.Code);
            if (p is null) { p = new Permission { Code = definition.Code, Description = definition.Description, IsActive = true, CreatedAt = DateTime.UtcNow }; db.Permissions.Add(p); }
            if (TenantDefaults.RolePermissions[AppRoles.Admin].Contains(p.Code)) db.RolePermissions.Add(new RolePermission { RoleId = admin.RoleId, Permission = p });
        }
        var platform1 = new PlatformUser { Username = "platform-530-a", Email = "platform530a@test.invalid", CreatedAt = DateTime.UtcNow };
        var platform2 = new PlatformUser { Username = "platform-530-b", Email = "platform530b@test.invalid", CreatedAt = DateTime.UtcNow };
        var hasher = new PasswordHasher<PlatformUser>();
        platform1.PasswordHash = hasher.HashPassword(platform1, PlatformPassword); platform2.PasswordHash = hasher.HashPassword(platform2, PlatformPassword);
        db.PlatformUsers.AddRange(platform1, platform2); await db.SaveChangesAsync();
        return (tenant, owner, platform1.Id, platform2.Id);
    }

    [Fact]
    public async Task Tenant_authority_scope_identity_and_legacy_reset_fail_closed()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var cashier = await Login(client, false, owner.Username, Legacy);
        var path = $"{TenantBase}/users/{owner.Id}";
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Post, path, null, IssueBody())).StatusCode);
        Assert.Equal(403, (int)(await Send(client, HttpMethod.Post, path, cashier, IssueBody())).StatusCode);
        await Error(await Send(client, HttpMethod.Post, path, admin, new IssueRecoveryDto("reason", "channel", false)), 400, "RECOVERY_IDENTITY_REQUIRED");
        var other = await TestDataBuilder.CreateTenantAsync(database, "other530", 531, 1m);
        await Error(await Send(client, HttpMethod.Post, $"{TenantBase}/users/{other.UserId}", admin, IssueBody()), 409, "RECOVERY_NOT_ALLOWED");
        await Error(await Send(client, HttpMethod.Put, $"/api/Users/{owner.Id}/password", admin, new { newPassword = NewPassword }), 409, "ASSISTED_RECOVERY_REQUIRED");
        await using var db = database.CreateDbContext();
        Assert.Equal(1, (await db.Users.SingleAsync(u => u.Id == owner.Id)).SessionVersion);
        var recovery = await Issue(client, false, owner.Id, admin);
        Assert.Equal("no-store", recovery.Response.Headers.CacheControl!.ToString());
        var challenge = await db.PasswordRecoveryChallenges.SingleAsync();
        Assert.Equal(32, challenge.TokenHash.Length); Assert.NotEqual(recovery.Value.Token, Convert.ToHexString(challenge.TokenHash));
        Assert.InRange((challenge.ExpiresAt - challenge.CreatedAt).TotalMinutes, 14.99, 15.01);
        Assert.False(TenantDefaults.RolePermissions[AppRoles.Cashier].Contains(AppPermissions.UsersRecoveryManage));
        Assert.False(TenantDefaults.RolePermissions[AppRoles.Supervisor].Contains(AppPermissions.UsersRecoveryManage));
        var audit = await db.PasswordSecurityAudits.SingleAsync();
        Assert.Equal("Issued", audit.Event); Assert.Equal(tenant.UserId, audit.ActorId); Assert.Equal(owner.Id, audit.TargetId);
        Assert.DoesNotContain(recovery.Value.Token, JsonSerializer.Serialize(audit));
        Assert.DoesNotContain(Legacy, JsonSerializer.Serialize(audit));
        Assert.DoesNotContain("token", await (await Send(client, HttpMethod.Get, path, admin)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recovery_is_session_independent_single_use_and_revokes_actual_old_JWT()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var old = await Login(client, false, owner.Username, Legacy);
        var issued = (await Issue(client, false, owner.Id, admin)).Value;
        Assert.Equal(204, (int)(await Complete(client, false, issued.Token)).StatusCode);
        await Error(await Complete(client, false, issued.Token), 400, PasswordRecoveryService.InvalidChallenge);
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", old)).StatusCode);
        Assert.Equal(401, (int)(await client.PostAsJsonAsync("/api/Auth/login", new { username = owner.Username, password = Legacy })).StatusCode);
        await Login(client, false, owner.Username, NewPassword);
        await using var db = database.CreateDbContext(); var user = await db.Users.SingleAsync(u => u.Id == owner.Id);
        Assert.Equal(2, user.SessionVersion); Assert.True(user.IsActive);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, NewPassword));
        Assert.Single(await db.PasswordSecurityAudits.Where(a => a.Event == "Consumed").ToListAsync());
    }

    [Theory]
    [InlineData("expired")][InlineData("revoked")][InlineData("tampered")][InlineData("malformed")][InlineData("unknown")]
    [InlineData("company")][InlineData("user")][InlineData("role")][InlineData("establishment")][InlineData("point")]
    [InlineData("session")][InlineData("role-version")][InlineData("password")][InlineData("bound-user")][InlineData("bound-company")]
    public async Task Invalid_or_stale_challenges_have_identical_public_error_without_state_changes(string kind)
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var issued = (await Issue(client, false, owner.Id, admin)).Value;
        await using var db = database.CreateDbContext();
        var c = await db.PasswordRecoveryChallenges.SingleAsync();
        var u = await db.Users.Include(u => u.Role).SingleAsync(u => u.Id == owner.Id);
        var token = issued.Token;
        switch (kind)
        {
            case "expired": c.CreatedAt = DateTime.UtcNow.AddMinutes(-30); c.ExpiresAt = DateTime.UtcNow.AddMinutes(-15); break;
            case "revoked": await Send(client, HttpMethod.Post, $"{TenantBase}/{c.Id}/revoke", admin); await db.Entry(c).ReloadAsync(); break;
            case "tampered": token = token[..^1] + (token[^1] == 'A' ? "B" : "A"); break;
            case "malformed": token = "not-a-secret"; break;
            case "unknown": token = $"t.{Guid.NewGuid():N}.{new string('A', 64)}"; break;
            case "company": (await db.Companies.SingleAsync()).IsActive = false; break;
            case "user": u.IsActive = false; break;
            case "role": u.Role.IsActive = false; break;
            case "establishment": (await db.Establishments.SingleAsync()).IsActive = false; break;
            case "point": (await db.EmissionPoints.SingleAsync()).IsActive = false; break;
            case "session": u.SessionVersion++; break;
            case "role-version": u.Role.AuthorizationVersion++; break;
            case "password": u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "other synthetic password"); break;
            case "bound-user": c.UserId = tenant.UserId; break;
            case "bound-company": var other = await TestDataBuilder.CreateTenantAsync(database, "scope530", 532, 1m); c.CompanyId = other.CompanyId; break;
        }
        await db.SaveChangesAsync(); var before = u.PasswordHash; var version = u.SessionVersion;
        await Error(await Complete(client, false, token), 400, PasswordRecoveryService.InvalidChallenge);
        await db.Entry(u).ReloadAsync(); await db.Entry(c).ReloadAsync();
        Assert.Equal(before, u.PasswordHash); Assert.Equal(version, u.SessionVersion); Assert.Null(c.ConsumedAt);
        Assert.True(await db.PasswordSecurityAudits.AnyAsync(a => a.Event == "Rejected"));
    }

    [Theory]
    [InlineData(11, false)][InlineData(12, true)][InlineData(256, true)][InlineData(257, false)]
    public async Task Self_change_requires_current_password_and_shared_policy_without_composition(int length, bool valid)
    {
        var (_, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var jwt = await Login(client, false, owner.Username, Legacy); var password = new string('a', length);
        var path = TenantBase + "/password";
        await Error(await Send(client, HttpMethod.Put, path, jwt, new SelfPasswordChangeDto("wrong", NewPassword)), 400, "CURRENT_PASSWORD_INVALID");
        var response = await Send(client, HttpMethod.Put, path, jwt, new SelfPasswordChangeDto(Legacy, password));
        if (valid) { Assert.Equal(204, (int)response.StatusCode); Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", jwt)).StatusCode); await Login(client, false, owner.Username, password); }
        else { await Error(response, 400, "PASSWORD_POLICY_INVALID"); await Login(client, false, owner.Username, Legacy); }
        await using var db = database.CreateDbContext();
        Assert.Equal(valid ? 2 : 1, (await db.Users.SingleAsync(u => u.Id == owner.Id)).SessionVersion);
        Assert.Equal(valid ? 1 : 0, await db.PasswordSecurityAudits.CountAsync(a => a.Event == "PasswordChanged"));
    }

    [Fact]
    public async Task Platform_is_separate_reauthenticated_other_account_only_and_session_revoking()
    {
        var (tenant, _, a, b) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, true, "platform-530-a", PlatformPassword); var old = await Login(client, true, "platform-530-b", PlatformPassword);
        var tenantJwt = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        Assert.NotEqual(200, (int)(await Send(client, HttpMethod.Post, $"{PlatformBase}/users/{b}", tenantJwt, IssueBody(PlatformPassword))).StatusCode);
        Assert.NotEqual(200, (int)(await Send(client, HttpMethod.Post, $"{TenantBase}/users/{tenant.UserId}", admin, IssueBody())).StatusCode);
        await Error(await Send(client, HttpMethod.Post, $"{PlatformBase}/users/{b}", admin, IssueBody("wrong")), 400, "CURRENT_PASSWORD_INVALID");
        await Error(await Send(client, HttpMethod.Post, $"{PlatformBase}/users/{a}", admin, IssueBody(PlatformPassword)), 409, "RECOVERY_NOT_ALLOWED");
        var issued = (await Issue(client, true, b, admin)).Value;
        await Error(await Complete(client, false, issued.Token), 400, PasswordRecoveryService.InvalidChallenge);
        Assert.Equal(204, (int)(await Complete(client, true, issued.Token)).StatusCode);
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/platform/auth/me", old)).StatusCode);
        var fresh = await Login(client, true, "platform-530-b", NewPassword);
        Assert.Equal(204, (int)(await Send(client, HttpMethod.Put, PlatformBase + "/password", fresh, new SelfPasswordChangeDto(NewPassword, PlatformPassword))).StatusCode);
        await using var db = database.CreateDbContext();
        Assert.Equal(2, await db.PlatformUsers.CountAsync(u => u.IsActive));
        Assert.All(await db.PasswordSecurityAudits.Where(a => a.IsPlatform).ToListAsync(), a => Assert.Null(a.CompanyId));
        Assert.True(await db.PasswordSecurityAudits.AnyAsync(a => a.IsPlatform && a.Event == "Consumed"));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Concurrent_independent_HTTP_consumers_have_exactly_one_success(bool platform)
    {
        var (tenant, owner, _, platformTarget) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, platform, platform ? "platform-530-a" : tenant.OperationalContext.Username, platform ? PlatformPassword : Legacy);
        var issued = (await Issue(client, platform, platform ? platformTarget : owner.Id, admin)).Value;
        await using var gate = database.CreateDbContext(); await using var tx = await gate.Database.BeginTransactionAsync();
        if (platform) await gate.Database.ExecuteSqlRawAsync("LOCK TABLE \"PlatformUsers\" IN SHARE ROW EXCLUSIVE MODE");
        else await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Companies\" WHERE \"Id\"={tenant.CompanyId} FOR UPDATE");
        var first = Complete(client, platform, issued.Token); var second = Complete(client, platform, issued.Token);
        try { await WaitForWaiters(2); } finally { await tx.CommitAsync(); }
        var statuses = new[] { (int)(await first).StatusCode, (int)(await second).StatusCode };
        Assert.Equal(new[] { 204, 400 }, statuses.Order().ToArray());
        await using var db = database.CreateDbContext();
        Assert.Equal(2, platform ? (await db.PlatformUsers.SingleAsync(u => u.Id == platformTarget)).SessionVersion : (await db.Users.SingleAsync(u => u.Id == owner.Id)).SessionVersion);
        Assert.Equal(1, await db.PasswordSecurityAudits.CountAsync(a => a.Event == "Consumed"));
    }

    [Fact]
    public async Task SUPERVISOR_existing_user_write_permission_does_not_authorize_recovery_or_revoke()
    {
        var (tenant, owner, _, b) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var issued = (await Issue(client, false, owner.Id, admin)).Value;
        await using var db = database.CreateDbContext(); var role = await db.Roles.SingleAsync(r => r.Id == owner.RoleId);
        role.Code = AppRoles.Supervisor;
        var permission = await db.Permissions.SingleAsync(p => p.Code == AppPermissions.AdminUsersWrite);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id }); await db.SaveChangesAsync();
        var supervisor = await Login(client, false, owner.Username, Legacy);
        Assert.Equal(403, (int)(await Send(client, HttpMethod.Post, $"{TenantBase}/users/{owner.Id}", supervisor, IssueBody())).StatusCode);
        Assert.Equal(403, (int)(await Send(client, HttpMethod.Post, $"{TenantBase}/{issued.Id}/revoke", supervisor)).StatusCode);
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Post, $"{PlatformBase}/users/{b}", null, IssueBody(PlatformPassword))).StatusCode);
    }

    [Fact]
    public async Task Operator_revoked_while_waiting_is_revalidated_after_lock()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        await using var gate = database.CreateDbContext(); await using var tx = await gate.Database.BeginTransactionAsync();
        await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Companies\" WHERE \"Id\"={tenant.CompanyId} FOR UPDATE");
        var pending = Send(client, HttpMethod.Post, $"{TenantBase}/users/{owner.Id}", admin, IssueBody());
        try { await WaitForWaiters(1); await gate.Users.Where(u => u.Id == tenant.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1)); }
        finally { await tx.CommitAsync(); }
        await Error(await pending, 401, "SESSION_STALE");
        await using var db = database.CreateDbContext(); Assert.Empty(await db.PasswordRecoveryChallenges.ToListAsync());
    }

    [Fact]
    public async Task Migration_grants_only_existing_ADMIN_and_bumps_only_new_assignments()
    {
        await using var db = database.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261005003734_AddCriticalOperationRequests");
        var t1 = await TestDataBuilder.CreateTenantAsync(database, "migration530a", 535, 1m);
        var t2 = await TestDataBuilder.CreateTenantAsync(database, "migration530b", 536, 1m);
        var first = await db.Roles.SingleAsync(r => r.CompanyId == t1.CompanyId); first.Code = AppRoles.Admin;
        var second = await db.Roles.SingleAsync(r => r.CompanyId == t2.CompanyId); second.Code = AppRoles.Admin;
        var supervisor = new Role { CompanyId = t1.CompanyId, Code = AppRoles.Supervisor, Name = "Supervisor", IsActive = true, CreatedAt = DateTime.UtcNow };
        var cashier = new Role { CompanyId = t1.CompanyId, Code = AppRoles.Cashier, Name = "Cashier", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Roles.AddRange(supervisor, cashier);
        var permission = new Permission { Code = AppPermissions.UsersRecoveryManage, Description = "Synthetic permission", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.RolePermissions.Add(new RolePermission { Role = second, Permission = permission }); await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(2, (await db.Roles.SingleAsync(r => r.Id == first.Id)).AuthorizationVersion);
        Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Id == second.Id)).AuthorizationVersion);
        Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Id == supervisor.Id)).AuthorizationVersion);
        Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Id == cashier.Id)).AuthorizationVersion);
        Assert.Equal(2, await db.RolePermissions.CountAsync(rp => rp.Permission.Code == AppPermissions.UsersRecoveryManage));
    }

    [Theory]
    [InlineData("company")][InlineData("user")][InlineData("role")][InlineData("establishment")][InlineData("point")]
    public async Task Issue_revalidates_every_target_lifecycle_state(string kind)
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        await using var db = database.CreateDbContext();
        if (kind == "company") (await db.Companies.SingleAsync()).IsActive = false;
        if (kind == "user") (await db.Users.SingleAsync(u => u.Id == owner.Id)).IsActive = false;
        if (kind == "role") (await db.Roles.SingleAsync(r => r.Id == owner.RoleId)).IsActive = false;
        if (kind == "establishment") (await db.Establishments.SingleAsync()).IsActive = false;
        if (kind == "point") (await db.EmissionPoints.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        var response = await Send(client, HttpMethod.Post, $"{TenantBase}/users/{owner.Id}", admin, IssueBody());
        Assert.Contains((int)response.StatusCode, new[] { 401, 409 }); Assert.Empty(await db.PasswordRecoveryChallenges.ToListAsync());
    }

    [Fact]
    public async Task Reissue_and_self_change_retire_old_links_without_extra_password_changes()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var old = (await Issue(client, false, owner.Id, admin)).Value; var current = (await Issue(client, false, owner.Id, admin)).Value;
        await Error(await Complete(client, false, old.Token), 400, PasswordRecoveryService.InvalidChallenge);
        var jwt = await Login(client, false, owner.Username, Legacy);
        Assert.Equal(204, (int)(await Send(client, HttpMethod.Put, TenantBase + "/password", jwt, new SelfPasswordChangeDto(Legacy, NewPassword))).StatusCode);
        await Error(await Complete(client, false, current.Token), 400, PasswordRecoveryService.InvalidChallenge);
        await using var db = database.CreateDbContext(); Assert.Equal(2, (await db.Users.SingleAsync(u => u.Id == owner.Id)).SessionVersion);
        Assert.All(await db.PasswordRecoveryChallenges.ToListAsync(), c => Assert.NotNull(c.RevokedAt));
    }

    [Theory]
    [InlineData("revoked")][InlineData("inactive")][InlineData("session")][InlineData("wrong-type")]
    public async Task Platform_stale_or_revoked_or_cross_plane_links_fail_generically(string kind)
    {
        var (tenant, owner, _, b) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, true, "platform-530-a", PlatformPassword);
        var issued = (await Issue(client, true, b, admin)).Value;
        await using var db = database.CreateDbContext();
        if (kind == "revoked") Assert.Equal(204, (int)(await Send(client, HttpMethod.Post, $"{PlatformBase}/{issued.Id}/revoke", admin, new SelfPasswordChangeDto(PlatformPassword, ""))).StatusCode);
        if (kind == "inactive") (await db.PlatformUsers.SingleAsync(u => u.Id == b)).IsActive = false;
        if (kind == "session") (await db.PlatformUsers.SingleAsync(u => u.Id == b)).SessionVersion++;
        await db.SaveChangesAsync();
        var token = issued.Token;
        if (kind == "wrong-type") {
            var tenantJwt = await Login(client, false, tenant.OperationalContext.Username, Legacy);
            token = (await Issue(client, false, owner.Id, tenantJwt)).Value.Token;
        }
        await Error(await Complete(client, true, token), 400, PasswordRecoveryService.InvalidChallenge);
        Assert.Null((await db.PasswordRecoveryChallenges.SingleAsync(c => c.Id == issued.Id)).ConsumedAt);
    }

    [Fact]
    public async Task Revoke_rechecks_permission_and_session_after_wait_not_cached_claims()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var issued = (await Issue(client, false, owner.Id, admin)).Value;
        await using var gate = database.CreateDbContext(); await using var tx = await gate.Database.BeginTransactionAsync();
        await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Companies\" WHERE \"Id\"={tenant.CompanyId} FOR UPDATE");
        var pending = Send(client, HttpMethod.Post, $"{TenantBase}/{issued.Id}/revoke", admin);
        try {
            await WaitForWaiters(1);
            await gate.RolePermissions.Where(rp => rp.Role.CompanyId == tenant.CompanyId && rp.Permission.Code == AppPermissions.UsersRecoveryManage).ExecuteDeleteAsync();
        } finally { await tx.CommitAsync(); }
        await Error(await pending, 403, "FORBIDDEN");
        await using var db = database.CreateDbContext(); Assert.Null((await db.PasswordRecoveryChallenges.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task Failed_success_audit_rolls_back_password_session_and_consumption()
    {
        var (tenant, owner, _, _) = await SeedAsync(database); using var f = Factory(); using var client = f.Client();
        var admin = await Login(client, false, tenant.OperationalContext.Username, Legacy);
        var issued = (await Issue(client, false, owner.Id, admin)).Value;
        await using var db = database.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"PasswordSecurityAudits\" ADD CONSTRAINT \"test_530_audit_failure\" CHECK (\"Event\" <> 'Consumed')");
        try { Assert.Equal(500, (int)(await Complete(client, false, issued.Token)).StatusCode); }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"PasswordSecurityAudits\" DROP CONSTRAINT \"test_530_audit_failure\""); }
        Assert.Equal(1, (await db.Users.SingleAsync(u => u.Id == owner.Id)).SessionVersion);
        Assert.Null((await db.PasswordRecoveryChallenges.SingleAsync()).ConsumedAt);
        await Login(client, false, owner.Username, Legacy);
        Assert.Equal(204, (int)(await Complete(client, false, issued.Token)).StatusCode);
    }

    private async Task WaitForWaiters(int count)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline) {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'", connection);
            if ((long)(await command.ExecuteScalarAsync())! >= count) return; await Task.Delay(30);
        }
        throw new TimeoutException("Recovery requests did not reach independent PostgreSQL lock waits.");
    }
    internal static IssueRecoveryDto IssueBody(string? password = null) => new("Synthetic identity procedure", "Accredited synthetic in-person channel", true, password);
    internal static async Task<string> Login(HttpClient client, bool platform, string username, string password)
    {
        var response = await client.PostAsJsonAsync(platform ? "/api/platform/auth/login" : "/api/Auth/login", new { username, password });
        Assert.Equal(200, (int)response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
    private static async Task<(RecoveryIssuedDto Value, HttpResponseMessage Response)> Issue(HttpClient client, bool platform, int id, string token)
    {
        var response = await Send(client, HttpMethod.Post, $"{(platform ? PlatformBase : TenantBase)}/users/{id}", token, IssueBody(platform ? PlatformPassword : null));
        Assert.Equal(200, (int)response.StatusCode); return ((await response.Content.ReadFromJsonAsync<RecoveryIssuedDto>())!, response);
    }
    internal static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string? jwt, object? body = null)
    {
        var request = new HttpRequestMessage(method, path); if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body is not null) request.Content = JsonContent.Create(body); return client.SendAsync(request);
    }
    private static Task<HttpResponseMessage> Complete(HttpClient client, bool platform, string token) => client.PostAsJsonAsync(
        (platform ? PlatformBase : TenantBase) + "/complete", new CompleteRecoveryDto(token, NewPassword));
    private static async Task Error(HttpResponseMessage response, int status, string code)
    { Assert.Equal(status, (int)response.StatusCode); Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()); }
}
