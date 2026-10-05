using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Xunit;

namespace Hfpos.FiscalSmokeHost;

internal static class SmokeConfiguration
{
    internal static bool CriticalOperations => Environment.GetEnvironmentVariable("HF_POS_SYNTHETIC_CRITICAL_HOST") == "1";
    internal static void Apply(IConfigurationBuilder configuration)
    {
        var connection = Environment.GetEnvironmentVariable("HF_POS_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("HF_POS_TEST_CONNECTION_STRING is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database != (CriticalOperations ? "hfpos_test_529" : "hfpos_test_528_smoke") || parsed.Username != "hfpos_test"
            || parsed.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Smoke host accepts only its dedicated loopback test database / hfpos_test user.");
        var work = Path.GetFullPath(Environment.GetEnvironmentVariable("HF_POS_SMOKE_WORKDIR")
            ?? Path.Combine(Path.GetTempPath(), CriticalOperations ? "hfpos-529-synthetic-smoke" : "hfpos-528-synthetic-smoke"));
        var keys = Directory.CreateDirectory(Path.Combine(work, "keys"));
        // No API appsettings, ambient configuration or command-line override is trusted by this test-only host.
        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["AllowedHosts"] = "localhost;127.0.0.1", ["SeedDemoData"] = "false",
            ["Jwt:Key"] = "hfpos-528-smoke-synthetic-only-key-never-used-in-real-environments",
            ["Jwt:Issuer"] = "hfpos-528-smoke", ["Jwt:Audience"] = "hfpos-528-smoke",
            ["Jwt:ExpiresMinutes"] = "60", ["Jwt:ClockSkewSeconds"] = "30",
            ["Sri:Environment"] = "1", ["Sri:EmissionType"] = "1", ["Sri:TimeoutSeconds"] = "30",
            ["Sri:AllowProductionSubmission"] = "false",
            ["Sri:ReceptionTestUrl"] = "https://synthetic-sri.invalid/never-called",
            ["Sri:AuthorizationTestUrl"] = "https://synthetic-sri.invalid/never-called",
            ["Sri:ReceptionProductionUrl"] = "https://synthetic-sri.invalid/never-called",
            ["Sri:AuthorizationProductionUrl"] = "https://synthetic-sri.invalid/never-called",
            ["DataProtection:KeysPath"] = keys.FullName, ["DataProtection:ApplicationName"] = "hfpos-528-smoke",
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            ["PlatformBootstrap:Enabled"] = "false", ["ForwardedHeaders_Enabled"] = "false",
            ["ForwardedHeaders:Enabled"] = "false", ["ForwardedHeaders:ForwardLimit"] = "1",
            ["AuthRateLimit:PermitLimit"] = "20", ["AuthRateLimit:WindowSeconds"] = "60",
            ["Transport:HstsMaxAgeDays"] = "30", ["Operations:ShutdownTimeoutSeconds"] = "30",
            ["Observability:Enabled"] = "false", ["Observability:ServiceName"] = "hfpos-528-smoke",
            ["Observability:TraceSampleRatio"] = "0.1"
        });
    }

    internal static void VerifyStartup()
    {
        // CreateBuilder exercises the same HostingStartup/content root as the human dotnet-run command.
        // No WebApplication.Build/Run, listener, hosted service or database connection is started.
        Assert.False(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")));
        var builder = WebApplication.CreateBuilder(new[]
        { "--Sri:AllowProductionSubmission=true", "--Jwt:ExpiresMinutes=0", "--SeedDemoData=true" });
        Assert.Equal("Testing", builder.Environment.EnvironmentName);
        Assert.Equal(60, builder.Configuration.GetValue<int>("Jwt:ExpiresMinutes"));
        Assert.False(builder.Configuration.GetValue<bool>("Sri:AllowProductionSubmission"));
        Assert.False(builder.Configuration.GetValue<bool>("SeedDemoData"));
        builder.Services.AddSecurityConfiguration(builder.Configuration, builder.Environment);
        builder.Services.AddOperations(builder.Configuration, builder.Environment);
        using var services = builder.Services.BuildServiceProvider();
        services.ValidateBeforeStartup();
        Assert.Equal(30, services.GetRequiredService<IOptions<OperationsOptions>>().Value.ShutdownTimeoutSeconds);
        Assert.False(services.GetRequiredService<IOptions<ObservabilityOptions>>().Value.Enabled);
        foreach (var endpoint in new[] { "ReceptionTestUrl", "AuthorizationTestUrl", "ReceptionProductionUrl", "AuthorizationProductionUrl" })
            Assert.Equal("synthetic-sri.invalid", new Uri(builder.Configuration[$"Sri:{endpoint}"]!).Host);
        Assert.Single(builder.Configuration.Sources);
        Console.WriteLine("Synthetic configuration PASS: human content root, all startup options, no appsettings dependency, unsafe overrides discarded; no API/DB started.");
    }
}
