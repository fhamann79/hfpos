using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class SessionVersioningHttpTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private const string Password = "test-only-password";
    private const string ChangedPassword = "new-test-only-password";
    private const string JwtKey = "hfpos-session-tests-only-signing-key-long-enough-for-hmac-sha256";
    private const string Issuer = "hfpos-session-tests";
    private const string Audience = "hfpos-session-tests";
    private static readonly DateTime CreatedAt = new(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string[] PermissionCodes =
    [
        AppPermissions.AdminRolesRead,
        AppPermissions.AdminRolesWrite,
        AppPermissions.AdminUsersRead,
        AppPermissions.AdminUsersWrite,
        AppPermissions.ReportsSalesRead
    ];

    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Development_seed_bumps_only_roles_receiving_missing_permissions_and_remains_idempotent()
    {
        await using var context = database.CreateDbContext();
        await SeedData.SeedDevelopmentAsync(context);
        var roles = await context.Roles.OrderBy(r => r.Code).ToListAsync();
        var original = roles.ToDictionary(r => r.Id, r => r.AuthorizationVersion);
        await SeedData.SeedDevelopmentAsync(context);
        Assert.All(roles, role => Assert.Equal(original[role.Id], role.AuthorizationVersion));

        var admin = roles.Single(r => r.Code == AppRoles.Admin);
        var permission = await context.Permissions.SingleAsync(p => p.Code == AppPermissions.AdminRolesRead);
        var assignment = await context.RolePermissions.SingleAsync(rp =>
            rp.RoleId == admin.Id && rp.PermissionId == permission.Id);
        context.RolePermissions.Remove(assignment);
        await context.SaveChangesAsync();

        await SeedData.SeedDevelopmentAsync(context);
        Assert.Equal(original[admin.Id] + 1, admin.AuthorizationVersion);
        Assert.All(roles.Where(r => r.Id != admin.Id),
            role => Assert.Equal(original[role.Id], role.AuthorizationVersion));
        Assert.True(await context.RolePermissions.AnyAsync(rp =>
            rp.RoleId == admin.Id && rp.PermissionId == permission.Id));
    }

    [Fact]
    public async Task Login_versions_are_current_and_missing_malformed_or_wrong_claims_fail_closed()
    {
        var tenant = await SeedTenantAsync("claims", 101);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, tenant.StaffName);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("1", jwt.Claims.Single(c => c.Type == AppClaims.UserSessionVersion).Value);
        Assert.Equal("1", jwt.Claims.Single(c => c.Type == AppClaims.RoleAuthorizationVersion).Value);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", token)).StatusCode);

        var malformed = new (string? User, string? Role)[]
        {
            (null, "1"), ("1", null), ("bad", "1"), ("1", "bad"),
            ("0", "1"), ("1", "-1"), ("2", "1"), ("1", "2")
        };
        foreach (var (userVersion, roleVersion) in malformed)
        {
            var response = await SendAsync(client, HttpMethod.Get, "/api/Auth/me",
                ResignWithVersions(token, userVersion, roleVersion));
            await AssertErrorAsync(response, HttpStatusCode.Unauthorized, "SESSION_STALE");
        }
    }

    [Fact]
    public async Task Permission_changes_stale_old_tokens_fresh_missing_permission_is_forbidden_and_no_op_keeps_token()
    {
        var tenant = await SeedTenantAsync("permissions", 102);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var oldStaff = await LoginAsync(client, tenant.StaffName);
        var roleUrl = $"/api/Roles/{tenant.StaffRoleId}/permissions";

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Users", oldStaff)).StatusCode);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { permissionIds = new[] { tenant.PermissionIds[AppPermissions.ReportsSalesRead] } }), HttpStatusCode.NoContent);
        Assert.Equal(2, await RoleVersionAsync(tenant.StaffRoleId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Users", oldStaff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");

        var noPermission = await LoginAsync(client, tenant.StaffName);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, "/api/Users", noPermission)).StatusCode);

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { permissionIds = new[] {
                tenant.PermissionIds[AppPermissions.ReportsSalesRead],
                tenant.PermissionIds[AppPermissions.AdminUsersRead] } }), HttpStatusCode.NoContent);
        Assert.Equal(3, await RoleVersionAsync(tenant.StaffRoleId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", noPermission),
            HttpStatusCode.Unauthorized, "SESSION_STALE");

        var fresh = await LoginAsync(client, tenant.StaffName);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Users", fresh)).StatusCode);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { permissionIds = new[] {
                tenant.PermissionIds[AppPermissions.AdminUsersRead],
                tenant.PermissionIds[AppPermissions.ReportsSalesRead],
                tenant.PermissionIds[AppPermissions.AdminUsersRead] } }), HttpStatusCode.NoContent);
        Assert.Equal(3, await RoleVersionAsync(tenant.StaffRoleId));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Users", fresh)).StatusCode);
    }

    [Fact]
    public async Task Password_and_user_context_changes_increment_once_and_email_only_and_no_op_do_not()
    {
        var tenant = await SeedTenantAsync("user-changes", 103);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var staff = await LoginAsync(client, tenant.StaffName);
        var userUrl = $"/api/Users/{tenant.StaffId}";

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl + "/password", admin,
            new { newPassword = ChangedPassword }), HttpStatusCode.NoContent);
        Assert.Equal(2, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponseAsync(client, tenant.StaffName, Password)).StatusCode);
        staff = await LoginAsync(client, tenant.StaffName, ChangedPassword);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff)).StatusCode);

        var changedEmail = UpdateBody(tenant, email: "changed@hfpos.test");
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin, changedEmail), HttpStatusCode.NoContent);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin, changedEmail), HttpStatusCode.NoContent);
        Assert.Equal(2, await UserVersionAsync(tenant.StaffId));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff)).StatusCode);

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin,
            UpdateBody(tenant, roleId: tenant.AlternateRoleId, email: "changed@hfpos.test")), HttpStatusCode.NoContent);
        Assert.Equal(3, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        staff = await LoginAsync(client, tenant.StaffName, ChangedPassword);

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin,
            UpdateBody(tenant, roleId: tenant.AlternateRoleId, emissionPointId: tenant.SecondEmissionPointId,
                email: "changed@hfpos.test")), HttpStatusCode.NoContent);
        Assert.Equal(4, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        staff = await LoginAsync(client, tenant.StaffName, ChangedPassword);

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin,
            UpdateBody(tenant, roleId: tenant.StaffRoleId, establishmentId: tenant.SecondEstablishmentId,
                emissionPointId: tenant.ThirdEmissionPointId, email: "changed@hfpos.test")), HttpStatusCode.NoContent);
        Assert.Equal(5, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        staff = await LoginAsync(client, tenant.StaffName, ChangedPassword);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff)).StatusCode);
    }

    [Fact]
    public async Task User_and_role_deactivation_reactivation_never_resurrect_old_tokens()
    {
        var tenant = await SeedTenantAsync("status", 104);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var staff = await LoginAsync(client, tenant.StaffName);
        var userUrl = $"/api/Users/{tenant.StaffId}";
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin,
            UpdateBody(tenant, isActive: false)), HttpStatusCode.NoContent);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, userUrl, admin,
            UpdateBody(tenant, isActive: true)), HttpStatusCode.NoContent);
        Assert.Equal(3, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        staff = await LoginAsync(client, tenant.StaffName);

        var roleUrl = $"/api/Roles/{tenant.StaffRoleId}";
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { name = "Staff", isActive = false }), HttpStatusCode.NoContent);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { name = "Staff", isActive = true }), HttpStatusCode.NoContent);
        Assert.Equal(3, await RoleVersionAsync(tenant.StaffRoleId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        var fresh = await LoginAsync(client, tenant.StaffName);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, roleUrl, admin,
            new { name = "Renamed staff", isActive = true }), HttpStatusCode.NoContent);
        Assert.Equal(3, await RoleVersionAsync(tenant.StaffRoleId));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", fresh)).StatusCode);
    }

    [Fact]
    public async Task Revoke_is_tenant_scoped_targeted_and_does_not_change_user_identity_fields()
    {
        var tenant = await SeedTenantAsync("revoke", 105);
        var otherTenant = await SeedTenantAsync("other", 106);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var staff = await LoginAsync(client, tenant.StaffName);
        var other = await LoginAsync(client, otherTenant.StaffName);
        var before = await ReadUserAsync(tenant.StaffId);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post,
            $"/api/Users/{tenant.StaffId}/revoke-sessions", staff)).StatusCode);
        Assert.Equal(1, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Post,
            $"/api/Users/{otherTenant.StaffId}/revoke-sessions", admin),
            HttpStatusCode.NotFound, "USER_NOT_FOUND");
        Assert.Equal(1, await UserVersionAsync(otherTenant.StaffId));
        await AssertStatusAsync(SendAsync(client, HttpMethod.Post,
            $"/api/Users/{tenant.StaffId}/revoke-sessions", admin), HttpStatusCode.NoContent);
        var after = await ReadUserAsync(tenant.StaffId);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.RoleId, after.RoleId);
        Assert.Equal(before.EstablishmentId, after.EstablishmentId);
        Assert.Equal(before.EmissionPointId, after.EmissionPointId);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(2, after.SessionVersion);
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", admin)).StatusCode);

        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, $"/api/Users/{tenant.StaffId}",
            admin, UpdateBody(tenant, isActive: false)), HttpStatusCode.NoContent);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Post,
            $"/api/Users/{tenant.StaffId}/revoke-sessions", admin), HttpStatusCode.NoContent);
        Assert.Equal(4, await UserVersionAsync(tenant.StaffId));
    }

    [Fact]
    public async Task Soft_delete_is_versioned_only_for_real_deactivation()
    {
        var tenant = await SeedTenantAsync("delete", 107);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var staff = await LoginAsync(client, tenant.StaffName);
        var url = $"/api/Users/{tenant.StaffId}";

        await AssertStatusAsync(SendAsync(client, HttpMethod.Delete, url, admin), HttpStatusCode.NoContent);
        await AssertStatusAsync(SendAsync(client, HttpMethod.Delete, url, admin), HttpStatusCode.NoContent);
        Assert.Equal(2, await UserVersionAsync(tenant.StaffId));
        await AssertErrorAsync(await SendAsync(client, HttpMethod.Get, "/api/Auth/me", staff),
            HttpStatusCode.Unauthorized, "SESSION_STALE");
    }

    [Fact]
    public async Task Last_active_admin_rejection_rolls_back_user_and_role_versions()
    {
        var tenant = await SeedTenantAsync("last-admin", 108);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var adminUrl = $"/api/Users/{tenant.AdminId}";

        await AssertErrorAsync(await SendAsync(client, HttpMethod.Put, adminUrl, admin,
            new { email = $"{tenant.AdminName}@hfpos.test", roleId = tenant.AdminRoleId,
                establishmentId = tenant.EstablishmentId, emissionPointId = tenant.EmissionPointId,
                isActive = false }), HttpStatusCode.Conflict, "LAST_ACTIVE_ADMIN_REQUIRED");
        var adminUser = await ReadUserAsync(tenant.AdminId);
        Assert.True(adminUser.IsActive);
        Assert.Equal(1, adminUser.SessionVersion);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", admin)).StatusCode);

        await AssertErrorAsync(await SendAsync(client, HttpMethod.Put,
            $"/api/Roles/{tenant.AdminRoleId}", admin,
            new { name = "Admin", isActive = false }), HttpStatusCode.Conflict, "LAST_ACTIVE_ADMIN_REQUIRED");
        Assert.Equal(1, await RoleVersionAsync(tenant.AdminRoleId));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Auth/me", admin)).StatusCode);
    }

    [Fact]
    public async Task Admin_root_permissions_are_immutable_but_other_roles_remain_configurable()
    {
        var tenant = await SeedTenantAsync("rbac", 109);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var adminUrl = $"/api/Roles/{tenant.AdminRoleId}/permissions";
        var originalIds = new[] {
            tenant.PermissionIds[AppPermissions.AdminRolesRead],
            tenant.PermissionIds[AppPermissions.AdminRolesWrite],
            tenant.PermissionIds[AppPermissions.AdminUsersRead],
            tenant.PermissionIds[AppPermissions.AdminUsersWrite] };
        foreach (var omitted in new[] { AppPermissions.AdminRolesRead, AppPermissions.AdminRolesWrite })
        {
            await AssertErrorAsync(await SendAsync(client, HttpMethod.Put, adminUrl, admin,
                new { permissionIds = originalIds.Where(id => id != tenant.PermissionIds[omitted]).ToArray() }),
                HttpStatusCode.Conflict, "ADMIN_ROLE_REQUIRED_PERMISSIONS");
            Assert.Equal(1, await RoleVersionAsync(tenant.AdminRoleId));
            Assert.Equal(originalIds.Order(), (await RolePermissionIdsAsync(tenant.AdminRoleId)).Order());
        }
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, adminUrl, admin,
            new { permissionIds = originalIds.Reverse().ToArray() }), HttpStatusCode.NoContent);
        Assert.Equal(1, await RoleVersionAsync(tenant.AdminRoleId));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/Users", admin)).StatusCode);

        var staffUrl = $"/api/Roles/{tenant.StaffRoleId}/permissions";
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, staffUrl, admin,
            new { permissionIds = new[] { tenant.PermissionIds[AppPermissions.AdminRolesRead] } }), HttpStatusCode.NoContent);
        Assert.Equal(2, await RoleVersionAsync(tenant.StaffRoleId));
        await AssertStatusAsync(SendAsync(client, HttpMethod.Put, staffUrl, admin,
            new { permissionIds = Array.Empty<int>() }), HttpStatusCode.NoContent);
        Assert.Equal(3, await RoleVersionAsync(tenant.StaffRoleId));
    }

    [Fact]
    public async Task Concurrent_revoke_requests_serialize_and_cannot_lose_an_increment()
    {
        var tenant = await SeedTenantAsync("concurrent", 110);
        using var factory = new SecurityApiFactory(database);
        using var client = factory.CreateClient();
        var admin = await LoginAsync(client, tenant.AdminName);
        var url = $"/api/Users/{tenant.StaffId}/revoke-sessions";

        await using var gate = new NpgsqlConnection(database.ConnectionString);
        await gate.OpenAsync();
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
            "SELECT 1 FROM \"Companies\" WHERE \"Id\" = @id FOR UPDATE", gate, tx))
        {
            command.Parameters.AddWithValue("id", tenant.CompanyId);
            await command.ExecuteNonQueryAsync();
        }

        var first = SendAsync(client, HttpMethod.Post, url, admin);
        var second = SendAsync(client, HttpMethod.Post, url, admin);
        try
        {
            await WaitForLockWaitersAsync(observer, 2);
        }
        finally
        {
            await tx.CommitAsync();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second).StatusCode);
        Assert.Equal(3, await UserVersionAsync(tenant.StaffId));
    }

    private async Task<Tenant> SeedTenantAsync(string suffix, int numericSeed)
    {
        await using var context = database.CreateDbContext();
        var company = new Company
        {
            Name = $"Security tenant {suffix}",
            Ruc = $"179{numericSeed:000000000}1",
            IsActive = true,
            CreatedAt = CreatedAt
        };
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var establishment = new Establishment
        {
            CompanyId = company.Id, Code = "001", Name = "Primary", Address = "Test address",
            IsActive = true, CreatedAt = CreatedAt
        };
        var secondEstablishment = new Establishment
        {
            CompanyId = company.Id, Code = "002", Name = "Secondary", Address = "Test address",
            IsActive = true, CreatedAt = CreatedAt
        };
        var adminRole = new Role { CompanyId = company.Id, Code = AppRoles.Admin, Name = "Admin", IsActive = true, CreatedAt = CreatedAt };
        var staffRole = new Role { CompanyId = company.Id, Code = AppRoles.Cashier, Name = "Staff", IsActive = true, CreatedAt = CreatedAt };
        var alternateRole = new Role { CompanyId = company.Id, Code = AppRoles.Supervisor, Name = "Alternate", IsActive = true, CreatedAt = CreatedAt };
        context.AddRange(establishment, secondEstablishment, adminRole, staffRole, alternateRole);
        await context.SaveChangesAsync();

        var emissionPoint = new EmissionPoint { EstablishmentId = establishment.Id, Code = "001", Name = "Primary", IsActive = true, CreatedAt = CreatedAt };
        var secondEmissionPoint = new EmissionPoint { EstablishmentId = establishment.Id, Code = "002", Name = "Second", IsActive = true, CreatedAt = CreatedAt };
        var thirdEmissionPoint = new EmissionPoint { EstablishmentId = secondEstablishment.Id, Code = "001", Name = "Third", IsActive = true, CreatedAt = CreatedAt };
        context.AddRange(emissionPoint, secondEmissionPoint, thirdEmissionPoint);

        var permissions = await context.Permissions.ToListAsync();
        if (permissions.Count == 0)
        {
            permissions = PermissionCodes.Select(code => new Permission
            {
                Code = code, Description = code, IsActive = true, CreatedAt = CreatedAt
            }).ToList();
            context.Permissions.AddRange(permissions);
        }
        await context.SaveChangesAsync();
        var permissionIds = permissions.ToDictionary(p => p.Code, p => p.Id);
        context.RolePermissions.AddRange(
            new[] { AppPermissions.AdminRolesRead, AppPermissions.AdminRolesWrite,
                AppPermissions.AdminUsersRead, AppPermissions.AdminUsersWrite }
                .Select(code => new RolePermission { RoleId = adminRole.Id, PermissionId = permissionIds[code] }));
        context.RolePermissions.AddRange(
            new[] { AppPermissions.AdminUsersRead, AppPermissions.ReportsSalesRead }
                .Select(code => new RolePermission { RoleId = staffRole.Id, PermissionId = permissionIds[code] }));

        var adminName = $"admin-{suffix}";
        var staffName = $"staff-{suffix}";
        var admin = NewUser(adminName, company.Id, adminRole.Id, establishment.Id, emissionPoint.Id);
        var staff = NewUser(staffName, company.Id, staffRole.Id, establishment.Id, emissionPoint.Id);
        context.Users.AddRange(admin, staff);
        await context.SaveChangesAsync();
        return new Tenant(company.Id, establishment.Id, emissionPoint.Id, secondEstablishment.Id,
            secondEmissionPoint.Id, thirdEmissionPoint.Id, adminRole.Id, staffRole.Id, alternateRole.Id,
            admin.Id, staff.Id, adminName, staffName, permissionIds);
    }

    private static User NewUser(string username, int companyId, int roleId, int establishmentId, int emissionPointId)
    {
        var user = new User
        {
            Username = username,
            Email = $"{username}@hfpos.test",
            CompanyId = companyId,
            RoleId = roleId,
            EstablishmentId = establishmentId,
            EmissionPointId = emissionPointId,
            IsActive = true,
            CreatedAt = CreatedAt
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        return user;
    }

    private static object UpdateBody(Tenant tenant, int? roleId = null, int? establishmentId = null,
        int? emissionPointId = null, string? email = null, bool isActive = true)
        => new
        {
            email = email ?? $"{tenant.StaffName}@hfpos.test",
            roleId = roleId ?? tenant.StaffRoleId,
            establishmentId = establishmentId ?? tenant.EstablishmentId,
            emissionPointId = emissionPointId ?? tenant.EmissionPointId,
            isActive
        };

    private async Task<User> ReadUserAsync(int userId)
    {
        await using var context = database.CreateDbContext();
        return await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task<long> UserVersionAsync(int userId) => (await ReadUserAsync(userId)).SessionVersion;

    private async Task<long> RoleVersionAsync(int roleId)
    {
        await using var context = database.CreateDbContext();
        return await context.Roles.Where(r => r.Id == roleId).Select(r => r.AuthorizationVersion).SingleAsync();
    }

    private async Task<int[]> RolePermissionIdsAsync(int roleId)
    {
        await using var context = database.CreateDbContext();
        return await context.RolePermissions.Where(rp => rp.RoleId == roleId).Select(rp => rp.PermissionId).ToArrayAsync();
    }

    private static async Task<HttpResponseMessage> LoginResponseAsync(HttpClient client, string username, string password)
        => await client.PostAsJsonAsync("/api/Auth/login", new { username, password });

    private static async Task<string> LoginAsync(HttpClient client, string username, string password = Password)
    {
        using var response = await LoginResponseAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method,
        string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(Task<HttpResponseMessage> responseTask, HttpStatusCode status)
    {
        using var response = await responseTask;
        Assert.Equal(status, response.StatusCode);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
        }
    }

    private static string ResignWithVersions(string token, string? userVersion, string? roleVersion)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claims = jwt.Claims
            .Where(c => c.Type != AppClaims.UserSessionVersion
                && c.Type != AppClaims.RoleAuthorizationVersion
                && c.Type != JwtRegisteredClaimNames.Exp
                && c.Type != JwtRegisteredClaimNames.Iat
                && c.Type != JwtRegisteredClaimNames.Nbf)
            .ToList();
        if (userVersion is not null) claims.Add(new Claim(AppClaims.UserSessionVersion, userVersion));
        if (roleVersion is not null) claims.Add(new Claim(AppClaims.RoleAuthorizationVersion, roleVersion));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var replacement = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(replacement);
    }

    private static async Task WaitForLockWaitersAsync(NpgsqlConnection connection, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
                connection);
            if ((long)(await command.ExecuteScalarAsync())! >= count) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected both administrative writes to wait on the tenant company lock.");
    }

    private sealed record Tenant(int CompanyId, int EstablishmentId, int EmissionPointId,
        int SecondEstablishmentId, int SecondEmissionPointId, int ThirdEmissionPointId,
        int AdminRoleId, int StaffRoleId, int AlternateRoleId, int AdminId, int StaffId,
        string AdminName, string StaffName, Dictionary<string, int> PermissionIds);

    private sealed class SecurityApiFactory(PostgresDatabaseFixture database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["SeedDemoData"] = "false",
                    ["Jwt:Key"] = JwtKey,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:ExpiresMinutes"] = "120"
                }));
        }
    }
}
