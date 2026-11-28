using System.Reflection;
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
        if (args.Contains("--release-pilot"))
        {
            try { await ReleasePilotSmoke.RunAsync(args); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"PILOT PROOF FAILED step={ReleasePilotSmoke.Step} type={error.GetType().Name}");
                Environment.ExitCode = 1;
            }
            return;
        }
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
                var fixtureContext = user.Id == fixture.Admin.UserId ? fixture.Admin : fixture.Tenant.OperationalContext;
                if (user.Id == fixtureContext.UserId)
                    fixtureContext.RoleAuthorizationVersion = user.Role.AuthorizationVersion;
            }
            await db.SaveChangesAsync();
            await SmokeTransport.InitializeAsync(connection);
            if (args.Contains("--fiscal-recovery"))
            {
                var sale = await fixture.SellAsync();
                var accessor = new StaticOperationalContextAccessor(fixture.Admin);
                await using var scope = new TestServiceScope(database, fixture.Admin);
                await using var fiscal = new FiscalWorkerTestScope(database.CreateDbContext(), protection,
                    fixture.Transport, fixture.Options, accessor, fixture.Sales(scope));
                await fiscal.Signing.SignInvoiceDraftAsync(sale.Id);
                fixture.Transport.Reception = _ => Task.FromException<Pos.Backend.Api.Core.Models.SriReceptionResponse>(
                    new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED"));
                try { await fiscal.Submission.SubmitSignedInvoiceAsync(sale.Id); }
                catch (InvalidOperationException ex) when (ex.Message == "SRI_RECEPTION_COMMUNICATION_FAILED") { }
                Console.WriteLine("Synthetic recovery fixture ready: one signed invoice with uncertain original reception.");
            }
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
            new object?[] { args.Where(a => a is not ("--initialize" or "--fiscal-recovery")).ToArray() });
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
            var certificate = options.ApplicationServices.GetRequiredService<RecoveryTlsCertificate>().Certificate;
            options.ListenLocalhost(7096, endpoint => endpoint.UseHttps(certificate));
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<RecoveryTlsCertificate>();
            services.AddSingleton<SmokeTransport>();
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, SyntheticHttpFilter>();
            if (SmokeConfiguration.CriticalOperations)
                services.AddSingleton<IStartupFilter, CriticalOperationsFaultFilter>();
            services.AddControllers().AddApplicationPart(typeof(SmokeStartup).Assembly)
                .AddApplicationPart(typeof(PosDbContext).Assembly);
        });
    }
}
