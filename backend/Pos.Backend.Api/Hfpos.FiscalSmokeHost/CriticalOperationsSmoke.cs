using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Hfpos.FiscalSmokeHost;

internal static class CriticalOperationsSmoke
{
    internal static async Task RunAsync(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(PostgresDatabaseFixture.ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException("HF_POS_TEST_CONNECTION_STRING is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database != "hfpos_test_529" || parsed.Username != "hfpos_test"
            || parsed.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("529 accepts only loopback hfpos_test_529 / hfpos_test.");
        void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);
        Set("ASPNETCORE_ENVIRONMENT", "Testing"); Set("DOTNET_ENVIRONMENT", "Testing");
        Set("HF_POS_SYNTHETIC_FISCAL_HOST", "1"); Set("HF_POS_SYNTHETIC_CRITICAL_HOST", "1");
        Set("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", typeof(SmokeMain).Assembly.GetName().Name!);
        Set("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "false");
        Set("HF_POS_SMOKE_WORKDIR", Environment.GetEnvironmentVariable("HF_POS_SMOKE_WORKDIR")
            ?? Path.Combine(Path.GetTempPath(), "hfpos-529-synthetic-smoke"));
        SmokeConfiguration.VerifyStartup();
        if (args.Contains("--verify-configuration")) return;
        if (args.Contains("--initialize"))
        {
            var database = new PostgresDatabaseFixture();
            await database.RecreateDatabaseAsync();
            var tenant = await TestDataBuilder.CreateTenantAsync(database, "critical-529", 9529, 10m, 10m);
            await using var db = database.CreateDbContext();
            var admin = new Role { CompanyId = tenant.CompanyId, Code = AppRoles.Admin, Name = "Synthetic admin",
                IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Roles.Add(admin); await db.SaveChangesAsync();
            var user = await db.Users.FindAsync(tenant.UserId);
            user!.RoleId = admin.Id;
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "synthetic-only-529-password");
            foreach (var field in typeof(AppPermissions).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var permission = new Permission { Code = (string)field.GetRawConstantValue()!, Description = "Synthetic smoke",
                    IsActive = true, CreatedAt = DateTime.UtcNow };
                db.Permissions.Add(permission);
                db.RolePermissions.Add(new RolePermission { RoleId = admin.Id, Permission = permission });
            }
            db.Suppliers.Add(new Supplier { CompanyId = tenant.CompanyId, Name = "Synthetic supplier 529",
                IsActive = true, CreatedAt = DateTime.UtcNow });
            var destination = new Establishment { CompanyId = tenant.CompanyId, Code = "002",
                Name = "Synthetic destination 529", Address = "Synthetic", IsActive = true, CreatedAt = DateTime.UtcNow };
            db.Establishments.Add(destination); await db.SaveChangesAsync();
            db.EmissionPoints.Add(new EmissionPoint { EstablishmentId = destination.Id, Code = "001",
                Name = "Synthetic destination point", IsActive = true, CreatedAt = DateTime.UtcNow });
            await using var tx = await db.Database.BeginTransactionAsync();
            var costs = new ProductCostService(db);
            foreach (var product in await db.Products.ToListAsync())
                costs.InitializeManualCost(product, tenant.UserId, DateTime.UtcNow);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            Console.WriteLine($"529 initialized: tenant={tenant.CompanyId}, establishment={tenant.EstablishmentId}, point={tenant.EmissionPointId}; stock=10 per product.");
        }
        Console.WriteLine("529 synthetic-only: https://localhost:7096. User cashier-critical-529; password synthetic-only-529-password.");
        var result = typeof(PosDbContext).Assembly.EntryPoint!.Invoke(null, new object?[] {
            args.Where(a => a is not ("--critical-operations" or "--initialize")).ToArray() });
        if (result is Task task) await task;
    }
}

// Header controls exist only in this disposable, test-only host, never in the production API.
internal sealed class CriticalOperationsFaultFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, proceed) =>
        {
            var path = context.Request.Path.Value ?? "";
            var allowed = path.StartsWith("/api/PurchaseReceipts", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/Inventory", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/CashSessions", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/PaymentSettlements", StringComparison.OrdinalIgnoreCase);
            var drop = allowed && HttpMethods.IsPost(context.Request.Method)
                && context.Request.Headers["X-Hfpos-Smoke-Drop"] == "after-commit";
            var delay = allowed && HttpMethods.IsGet(context.Request.Method)
                && int.TryParse(context.Request.Headers["X-Hfpos-Smoke-Delay"], out var milliseconds)
                ? Math.Clamp(milliseconds, 0, 8000) : 0;
            if (!drop && delay == 0) { await proceed(); return; }
            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await proceed();
                if (drop && context.Response.StatusCode is >= 200 and < 300)
                {
                    context.Abort();
                    return;
                }
                if (delay > 0) await Task.Delay(delay);
                buffer.Position = 0;
                await buffer.CopyToAsync(original);
            }
            finally { context.Response.Body = original; }
        });
        next(app);
    };
}
