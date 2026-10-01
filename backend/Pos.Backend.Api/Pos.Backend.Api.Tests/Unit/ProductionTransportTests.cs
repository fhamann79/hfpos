using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class ProductionTransportTests
{
    [Fact]
    public async Task Production_starts_without_database_connection_migrations_or_demo_seed_and_has_security_headers()
    {
        // Port 1 cannot serve PostgreSQL: startup must not connect/migrate with bootstrap disabled.
        using var factory = new ProductionSecurityApiFactory();
        using var client = factory.Client();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var hsts = response.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=2592000", hsts);
        Assert.DoesNotContain("preload", hsts); Assert.DoesNotContain("includeSubDomains", hsts);
        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    [Theory]
    [InlineData("Jwt:Key", "short")]
    [InlineData("SeedDemoData", "true")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("ForwardedHeaders_Enabled", "true")]
    public void Real_program_rejects_invalid_production_before_database_use(string key, string value)
    {
        using var factory = new ProductionSecurityApiFactory(new() { [key] = value });
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    [Theory]
    [InlineData("https://allowed.test", "https://allowed.test", true)]
    [InlineData("https://allowed.test/", "https://allowed.test", true)]
    [InlineData("https://allowed.test", "https://other.test", false)]
    [InlineData(null, "https://other.test", false)]
    public async Task Cors_returns_headers_only_for_explicit_allowed_origins(string? allowed, string origin, bool accepted)
    {
        using var factory = new ProductionSecurityApiFactory(new() { ["Cors:AllowedOrigins:0"] = allowed });
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/Auth/login");
        request.Headers.Add("Origin", origin); request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var response = await client.SendAsync(request);
        Assert.Equal(accepted, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (accepted) Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData(false, "192.0.2.10", false)]
    [InlineData(true, "192.0.2.20", false)]
    [InlineData(true, "192.0.2.10", true)]
    public async Task Forwarded_proto_is_used_only_from_an_explicit_trusted_proxy(bool enabled, string remote, bool trusted)
    {
        using var factory = new ProductionSecurityApiFactory(new()
        { ["ForwardedHeaders:Enabled"] = enabled.ToString(), ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10" }, System.Net.IPAddress.Parse(remote));
        using var client = factory.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-For", "198.51.100.20"); request.Headers.Add("X-Forwarded-Proto", "http");
        var response = await client.SendAsync(request);
        // The incoming transport is HTTPS; only a trusted proxy can change it to HTTP.
        Assert.Equal(!trusted, response.Headers.Contains("Strict-Transport-Security"));
    }
}
