using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class ProductionConfigurationTests : IDisposable
{
    private readonly string _keys = Directory.CreateTempSubdirectory("hfpos-519-config-").FullName;
    public void Dispose() => Directory.Delete(_keys, true);

    internal static Dictionary<string, string?> Valid(string keys) => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=hfpos_test_unreachable;Username=synthetic;Password=test-only;Timeout=1",
        ["Jwt:Key"] = "synthetic-tests-only-519-not-a-production-signing-key",
        ["Jwt:Issuer"] = "synthetic-519", ["Jwt:Audience"] = "synthetic-519", ["Jwt:ExpiresMinutes"] = "120",
        ["AllowedHosts"] = "example.test", ["SeedDemoData"] = "false",
        ["DataProtection:ApplicationName"] = "HFPOS-SYNTHETIC", ["DataProtection:KeysPath"] = keys,
        ["Sri:ReceptionTestUrl"] = "https://reception.test.invalid/soap",
        ["Sri:AuthorizationTestUrl"] = "https://authorization.test.invalid/soap",
        ["PlatformBootstrap:Enabled"] = "false"
    };

    [Fact]
    public void Synthetic_production_and_empty_cors_and_disabled_bootstrap_are_valid()
    {
        using var services = Provider(Valid(_keys));
        services.ValidateBeforeStartup();
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection", "")]
    [InlineData("Jwt:Key", "")]
    [InlineData("Jwt:Key", "short-test-key")]
    [InlineData("Jwt:Issuer", " ")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Jwt:ExpiresMinutes", "4")]
    [InlineData("Jwt:ExpiresMinutes", "1441")]
    [InlineData("Jwt:ClockSkewSeconds", "-1")]
    [InlineData("Jwt:ClockSkewSeconds", "121")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "example.test;*.example.test")]
    [InlineData("AllowedHosts", "")]
    [InlineData("Cors:AllowedOrigins:0", "https://*.test.invalid")]
    [InlineData("Cors:AllowedOrigins:0", "http://example.test")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.test/path")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.test/?x=1")]
    [InlineData("Cors:AllowedOrigins:0", "https://example.test/#fragment")]
    [InlineData("DataProtection:ApplicationName", "")]
    [InlineData("DataProtection:KeysPath", "")]
    [InlineData("DataProtection:KeysPath", "relative-folder")]
    [InlineData("ForwardedHeaders:Enabled", "true")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:ForwardLimit", "0")]
    [InlineData("ForwardedHeaders:ForwardLimit", "4")]
    [InlineData("AuthRateLimit:PermitLimit", "0")]
    [InlineData("AuthRateLimit:WindowSeconds", "0")]
    [InlineData("SeedDemoData", "true")]
    [InlineData("ForwardedHeaders_Enabled", "true")]
    [InlineData("PlatformBootstrap:Enabled", "true")]
    [InlineData("Sri:AllowProductionSubmission", "true")]
    [InlineData("Sri:ReceptionTestUrl", "http://example.test/soap")]
    [InlineData("Sri:AuthorizationTestUrl", "not-a-uri")]
    [InlineData("Sri:TimeoutSeconds", "121")]
    [InlineData("Sri:ReceptionProductionUrl", "http://example.test/soap")]
    [InlineData("Sri:AuthorizationProductionUrl", "not-a-uri")]
    [InlineData("Sri:Environment", "3")]
    [InlineData("Transport:HstsMaxAgeDays", "0")]
    public void Invalid_production_fails_closed_without_disclosing_values(string key, string value)
    {
        var settings = Valid(_keys);
        settings[key] = value;
        using var provider = Provider(settings);
        var exception = Assert.Throws<OptionsValidationException>(() => provider.ValidateBeforeStartup());
        if (!string.IsNullOrEmpty(settings["Jwt:Key"])) Assert.DoesNotContain(settings["Jwt:Key"]!, exception.Message);
        Assert.DoesNotContain("Password=test-only", exception.Message);
    }

    [Fact]
    public void Missing_mount_is_rejected_and_not_created()
    {
        var missing = Path.Combine(_keys, "missing-mount");
        var settings = Valid(_keys); settings["DataProtection:KeysPath"] = missing;
        using var provider = Provider(settings);
        Assert.Throws<OptionsValidationException>(() => provider.ValidateBeforeStartup());
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Explicit_https_origin_proxy_and_bootstrap_are_valid()
    {
        var settings = Valid(_keys);
        settings["Cors:AllowedOrigins:0"] = "https://example.test/";
        settings["ForwardedHeaders:Enabled"] = "true";
        settings["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10";
        settings["PlatformBootstrap:Enabled"] = "true";
        settings["PlatformBootstrap:Username"] = "synthetic-admin";
        settings["PlatformBootstrap:Email"] = "admin@test.invalid";
        settings["PlatformBootstrap:Password"] = "synthetic-password-only";
        using var services = Provider(settings); services.ValidateBeforeStartup();
    }

    [Fact]
    public void Development_preserves_implicit_key_ring_local_cors_and_demo_seed()
    {
        var settings = Valid(_keys);
        settings["DataProtection:KeysPath"] = ""; settings["AllowedHosts"] = "*";
        settings["SeedDemoData"] = "true"; settings["Cors:AllowedOrigins:0"] = "http://localhost:4200";
        using var services = Provider(settings, "Development"); services.ValidateBeforeStartup();
        Assert.Null(services.GetRequiredService<IOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>>().Value.XmlRepository);
        Assert.NotEqual("HFPOS-SYNTHETIC", services.GetRequiredService<IOptions<Microsoft.AspNetCore.DataProtection.DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("http://production.invalid/r", "https://production.invalid/a")]
    [InlineData("https://reception.test.invalid/soap", "https://production.invalid/a")]
    public void Production_sri_urls_must_be_secure_and_distinct(string reception, string authorization)
    {
        var settings = Valid(_keys); settings["Sri:AllowProductionSubmission"] = "true";
        settings["Sri:ReceptionProductionUrl"] = reception; settings["Sri:AuthorizationProductionUrl"] = authorization;
        using var provider = Provider(settings);
        Assert.Throws<OptionsValidationException>(() => provider.ValidateBeforeStartup());
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, string environment = "Production")
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSecurityConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new SyntheticEnvironment(environment));
        return services.BuildServiceProvider();
    }
}

internal sealed class SyntheticEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;
    public string ApplicationName { get; set; } = "Synthetic.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
