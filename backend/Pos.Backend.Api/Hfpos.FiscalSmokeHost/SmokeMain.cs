using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using Npgsql;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;

[assembly: HostingStartup(typeof(Hfpos.FiscalSmokeHost.SmokeStartup))]

namespace Hfpos.FiscalSmokeHost;

public static class SmokeMain
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--assisted-recovery"))
        {
            await AssistedRecoverySmoke.RunAsync(args);
            return;
        }
        if (args.Contains("--critical-operations"))
        {
            await CriticalOperationsSmoke.RunAsync(args);
            return;
        }
        var connection = Environment.GetEnvironmentVariable(PostgresDatabaseFixture.ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException("HF_POS_TEST_CONNECTION_STRING is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database != "hfpos_test_528_smoke" || parsed.Username != "hfpos_test" || parsed.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Smoke host accepts only the dedicated loopback hfpos_test_528_smoke database/user.");
        var work = Path.GetFullPath(Environment.GetEnvironmentVariable("HF_POS_SMOKE_WORKDIR")
            ?? Path.Combine(Path.GetTempPath(), "hfpos-528-synthetic-smoke"));
        var keys = Directory.CreateDirectory(Path.Combine(work, "keys"));
        void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);
        Set("ASPNETCORE_ENVIRONMENT", "Testing"); Set("DOTNET_ENVIRONMENT", "Testing");
        Set("HF_POS_SYNTHETIC_FISCAL_HOST", "1");
        Set("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(SmokeMain).Assembly.GetName().Name!);
        Set("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");
        if (args.Contains("--verify-configuration"))
        {
            SmokeConfiguration.VerifyStartup();
            return;
        }
        // Validate the exact human startup configuration before any disposable fixture initialization.
        SmokeConfiguration.VerifyStartup();
        AutonomousIssuingFixture? initialized = null;
        if (args.Contains("--initialize"))
        {
            var database = new PostgresDatabaseFixture();
            await database.RecreateDatabaseAsync(); // Explicit initialize only; restarts preserve the database and key ring.
            var protection = DataProtectionProvider.Create(keys, b => b.SetApplicationName("hfpos-528-smoke"));
            var fixture = new AutonomousIssuingFixture(database, protection);
            initialized = fixture;
            await fixture.InitializeAsync("autonomous-smoke", 928);
            await fixture.SetDelegationAsync(false);
            await using var db = database.CreateDbContext();
            var codes = typeof(AppPermissions).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (string)f.GetRawConstantValue()!).ToArray();
            foreach (var code in codes)
                if (!await db.Permissions.AnyAsync(p => p.Code == code))
                    db.Permissions.Add(new Permission { Code = code, Description = code, IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            foreach (var user in await db.Users.Include(u => u.Role).ToListAsync())
            {
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "synthetic-only-528-password");
                var reads = new[] { AppPermissions.CashSessionsRead, AppPermissions.CatalogProductsRead,
                    AppPermissions.CustomersRead, AppPermissions.InventoryRead, AppPermissions.OpStructureRead };
                var permissions = await db.Permissions.Where(p => user.Role.Code == AppRoles.Admin || reads.Contains(p.Code)).ToListAsync();
                foreach (var permission in permissions)
                    if (!await db.RolePermissions.AnyAsync(rp => rp.RoleId == user.RoleId && rp.PermissionId == permission.Id))
                        db.RolePermissions.Add(new RolePermission { RoleId = user.RoleId, PermissionId = permission.Id });
                user.Role.AuthorizationVersion++;
            }
            await db.SaveChangesAsync();
            await SmokeTransport.InitializeAsync(connection);
            Console.WriteLine($"Synthetic tenant={fixture.Tenant.CompanyId}, establishment={fixture.Tenant.EstablishmentId}, point={fixture.Tenant.EmissionPointId}. Delegation OFF.");
        }
        if (args.Contains("--verify"))
        {
            if (initialized is null) throw new InvalidOperationException("Verification requires --initialize on a disposable database.");
            await SmokeVerification.RunAsync(initialized, connection);
            return;
        }
        Console.WriteLine("Synthetic-only host: https://localhost:7096. Admin fiscal-autonomous-smoke; cashier cashier-autonomous-smoke; password synthetic-only-528-password.");
        var result = typeof(PosDbContext).Assembly.EntryPoint!.Invoke(null,
            new object?[] { args.Where(a => a != "--initialize").ToArray() });
        if (result is Task task) await task;
    }
}

public sealed class SmokeStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        if (Environment.GetEnvironmentVariable("HF_POS_SYNTHETIC_FISCAL_HOST") != "1"
            || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing")
            throw new InvalidOperationException("Synthetic hosting startup is test-only.");
        builder.ConfigureAppConfiguration((context, configuration) =>
        {
            if (!context.HostingEnvironment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Synthetic hosting startup requires the Testing environment.");
            SmokeConfiguration.Apply(configuration);
        });
        builder.ConfigureKestrel(options =>
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost synthetic HFPOS", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.EphemeralKeySet);
            options.ListenLocalhost(7096, endpoint => endpoint.UseHttps(certificate));
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<SmokeTransport>();
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, SyntheticHttpFilter>();
            if (SmokeConfiguration.CriticalOperations)
                services.AddSingleton<IStartupFilter, CriticalOperationsFaultFilter>();
            services.AddControllers().AddApplicationPart(typeof(SmokeStartup).Assembly)
                .AddApplicationPart(typeof(PosDbContext).Assembly);
        });
    }
}
