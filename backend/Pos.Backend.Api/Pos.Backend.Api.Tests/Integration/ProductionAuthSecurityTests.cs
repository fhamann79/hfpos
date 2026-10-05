using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ProductionAuthSecurityTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private const string LegacyPassword = "old-only";
    private const string PlatformPassword = "synthetic-platform-password-only";
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private ProductionSecurityApiFactory Factory(int limit = 100, IPAddress? remoteIp = null, bool proxy = false) => new(new()
    {
        ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
        ["AuthRateLimit:PermitLimit"] = limit.ToString(), ["AuthRateLimit:WindowSeconds"] = "60",
        ["ForwardedHeaders:Enabled"] = proxy.ToString(), ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10"
    }, remoteIp);

    private async Task<(TestTenant Tenant, string Hash)> SeedAsync()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "security519", 519, 1m);
        await using var context = database.CreateDbContext();
        var user = await context.Users.Include(u => u.Role).SingleAsync(u => u.Id == tenant.UserId);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, LegacyPassword);
        user.Role.Code = AppRoles.Admin;
        foreach (var code in new[] { AppPermissions.AdminUsersWrite, AppPermissions.AdminUsersRead })
        {
            var permission = new Permission { Code = code, Description = "Synthetic permission", IsActive = true, CreatedAt = DateTime.UtcNow };
            context.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, Permission = permission });
        }
        await context.SaveChangesAsync();
        await PlatformBootstrap.RunAsync(context, new PlatformBootstrapOptions
        { Enabled = true, Username = "platform-synthetic", Email = "platform@test.invalid", Password = PlatformPassword });
        return (tenant, user.PasswordHash);
    }

    [Fact]
    public async Task Unknown_wrong_and_inactive_wrong_passwords_have_identical_public_response_and_legacy_login_survives()
    {
        var (tenant, hash) = await SeedAsync();
        using var factory = Factory(); using var client = factory.Client();
        var username = tenant.OperationalContext.Username;
        var absent = await Login(client, "/api/Auth/login", "absent-synthetic", "wrong-only");
        var wrong = await Login(client, "/api/Auth/login", username, "wrong-only");
        await Error(absent, 401, "INVALID_CREDENTIALS"); await Error(wrong, 401, "INVALID_CREDENTIALS");
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        var correct = await Login(client, "/api/Auth/login", $" {username} ", LegacyPassword);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Equal("no-store", correct.Headers.CacheControl!.ToString());
        await using var context = database.CreateDbContext();
        var user = await context.Users.SingleAsync(); Assert.Equal(hash, user.PasswordHash); Assert.Equal(1, user.SessionVersion);
        user.IsActive = false; await context.SaveChangesAsync();
        await Error(await Login(client, "/api/Auth/login", username, "wrong-only"), 401, "INVALID_CREDENTIALS");
        Assert.Equal(hash, user.PasswordHash);
    }

    [Fact]
    public async Task Suspended_company_rejects_correct_credentials_but_does_not_leak_state_for_wrong_credentials()
    {
        var (tenant, _) = await SeedAsync();
        await using var context = database.CreateDbContext();
        (await context.Companies.SingleAsync()).IsActive = false; await context.SaveChangesAsync();
        using var factory = Factory(); using var client = factory.Client();
        await Error(await Login(client, "/api/Auth/login", tenant.OperationalContext.Username, "wrong-only"), 401, "INVALID_CREDENTIALS");
        await Error(await Login(client, "/api/Auth/login", tenant.OperationalContext.Username, LegacyPassword), 401, "COMPANY_INACTIVE_OR_NOT_FOUND");
    }

    [Fact]
    public async Task Platform_and_tenant_tokens_remain_isolated_and_platform_credentials_are_generic()
    {
        var (tenant, _) = await SeedAsync(); using var factory = Factory(); using var client = factory.Client();
        await Error(await Login(client, "/api/platform/auth/login", "missing-synthetic", "wrong-only"), 401, "INVALID_CREDENTIALS");
        await Error(await Login(client, "/api/platform/auth/login", "platform-synthetic", "wrong-only"), 401, "INVALID_CREDENTIALS");
        var platformResponse = await Login(client, "/api/platform/auth/login", "platform-synthetic", PlatformPassword);
        Assert.Equal("no-store", platformResponse.Headers.CacheControl!.ToString());
        var platform = await Token(platformResponse);
        var tenantToken = await Token(await Login(client, "/api/Auth/login", tenant.OperationalContext.Username, LegacyPassword));
        Assert.Equal(200, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", tenantToken)).StatusCode);
        Assert.Equal(200, (int)(await Send(client, HttpMethod.Get, "/api/platform/auth/me", platform)).StatusCode);
        Assert.NotEqual(200, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", platform)).StatusCode);
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/platform/auth/me", tenantToken)).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_is_per_plane_with_json_retry_after_and_no_secrets()
    {
        using var factory = Factory(2); using var client = factory.Client();
        foreach (var path in new[] { "/api/Auth/login", "/api/platform/auth/login" })
        {
            for (var i = 0; i < 2; i++) await Error(await Login(client, path, "absent", "synthetic-secret-password"), 401, "INVALID_CREDENTIALS");
            var rejected = await Login(client, path, "absent", "synthetic-secret-password");
            await Error(rejected, 429, "RATE_LIMITED");
            Assert.Equal("application/json", rejected.Content.Headers.ContentType!.MediaType);
            Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
            Assert.DoesNotContain("synthetic-secret-password", await rejected.Content.ReadAsStringAsync());
            Assert.Equal("no-store", rejected.Headers.CacheControl!.ToString());
            await Error(await Login(client, path.ToUpperInvariant() + "/", "absent", "synthetic-secret-password"), 429, "RATE_LIMITED");
        }
    }

    [Theory]
    [InlineData("192.0.2.10", true)]
    [InlineData("192.0.2.20", false)]
    public async Task Only_trusted_forwarded_client_ips_partition_the_login_limiter(string remote, bool trusted)
    {
        using var factory = Factory(1, IPAddress.Parse(remote), true); using var client = factory.Client();
        async Task<HttpResponseMessage> Attempt(string forwarded)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/login")
            { Content = JsonContent.Create(new { username = "absent", password = "synthetic-only" }) };
            request.Headers.Add("X-Forwarded-For", forwarded); return await client.SendAsync(request);
        }
        await Error(await Attempt("198.51.100.1"), 401, "INVALID_CREDENTIALS");
        await Error(await Attempt("198.51.100.2"), trusted ? 401 : 429, trusted ? "INVALID_CREDENTIALS" : "RATE_LIMITED");
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(257, false)]
    public async Task User_create_and_change_password_enforce_policy_and_revoke_only_successful_changes(int length, bool valid)
    {
        var (tenant, originalHash) = await SeedAsync(); using var factory = Factory(); using var client = factory.Client();
        var token = await Token(await Login(client, "/api/Auth/login", tenant.OperationalContext.Username, LegacyPassword));
        await using var context = database.CreateDbContext(); var user = await context.Users.AsNoTracking().SingleAsync();
        var password = new string('a', length);
        var create = await Send(client, HttpMethod.Post, "/api/Users", token, new
        { username = "new-synthetic", email = "new@test.invalid", password, user.RoleId, tenant.EstablishmentId, tenant.EmissionPointId, isActive = true });
        if (valid) Assert.Equal(201, (int)create.StatusCode); else await Error(create, 400, "PASSWORD_POLICY_INVALID");
        var change = await Send(client, HttpMethod.Put, "/api/account/recovery/password", token, new { currentPassword = LegacyPassword, newPassword = password });
        if (valid) Assert.Equal(204, (int)change.StatusCode); else await Error(change, 400, "PASSWORD_POLICY_INVALID");
        var after = await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(valid ? 2 : 1, after.SessionVersion);
        if (valid)
        {
            Assert.NotEqual(originalHash, after.PasswordHash);
            Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", token)).StatusCode);
            Assert.Equal(200, (int)(await Login(client, "/api/Auth/login", user.Username, password)).StatusCode);
        }
        else { Assert.Equal(originalHash, after.PasswordHash); Assert.Equal(1, await context.Users.CountAsync()); }
    }

    [Theory]
    [InlineData("HS384")]
    [InlineData("none")]
    [InlineData("expired")]
    [InlineData("missing-expiration")]
    public async Task Jwt_rejects_wrong_algorithm_unsigned_expired_or_missing_expiration(string kind)
    {
        var (tenant, _) = await SeedAsync(); using var factory = Factory(); using var client = factory.Client();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(await Token(await Login(client, "/api/Auth/login", tenant.OperationalContext.Username, LegacyPassword)));
        var jwt = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key));
        var credentials = kind == "none" ? null : new SigningCredentials(key, kind == "HS384" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        var changed = new JwtSecurityToken(jwt.Issuer, jwt.Audience, token.Claims.Where(c => c.Type is not ("exp" or "nbf" or "iat")),
            expires: kind == "missing-expiration" ? null : kind == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        Assert.Equal(401, (int)(await Send(client, HttpMethod.Get, "/api/Auth/me", new JwtSecurityTokenHandler().WriteToken(changed))).StatusCode);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string path, string username, string password)
        => client.PostAsJsonAsync(path, new { username, password });
    private static async Task<string> Token(HttpResponseMessage response)
    { Assert.Equal(200, (int)response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("token").GetString()!; }
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body); return client.SendAsync(request);
    }
    private static async Task Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body); Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("companyId", body); Assert.DoesNotContain("userId", body); Assert.DoesNotContain("password", body); Assert.DoesNotContain("token", body);
    }
}
