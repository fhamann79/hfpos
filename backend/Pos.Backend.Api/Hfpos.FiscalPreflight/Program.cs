using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;

// NOT the Web API entrypoint: no auth/seed/write probe/business workers/HTTP host.
static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Required operational input: {name}");
if (args is not ["--read-only-cycle"])
    throw new InvalidOperationException("Only --read-only-cycle is supported; see RC6 runbook.");
var connection = Required("HF_RC6_READONLY_CONNECTION");
var keys = new DirectoryInfo(Required("HF_RC6_READONLY_KEYRING"));
if (!keys.Exists) throw new InvalidOperationException("Existing read-only keyring required.");
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Error));
services.AddDbContext<PosDbContext>(o => o.UseNpgsql(connection));
services.AddDataProtection().PersistKeysToFileSystem(keys).SetApplicationName(Required("HF_RC6_APPLICATION_NAME"))
    .DisableAutomaticKeyGeneration();
services.Configure<FiscalTrustOptions>(o =>
{
    o.RegistryPath = Required("HF_RC6_REGISTRY_PATH");
    o.ExpectedSha256 = Required("HF_RC6_APPROVED_SHA256");
});
services.AddSingleton<FiscalTrustRegistry>();
services.AddSingleton<IFiscalCertificateEvaluator, FiscalCertificateEvaluator>();
services.AddSingleton<FiscalPreflightCache>(); services.AddSingleton<FiscalPublicChainCache>();
services.AddScoped<FiscalTrustMonitor>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
await db.Database.OpenConnectionAsync();
await using var check = db.Database.GetDbConnection().CreateCommand();
check.CommandText = """
    SELECT current_setting('default_transaction_read_only') = 'on'
      AND NOT r.rolsuper AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolbypassrls
      AND NOT EXISTS (
        SELECT 1 FROM pg_tables t WHERE t.schemaname = 'public'
        AND has_table_privilege(current_user, quote_ident(t.schemaname)||'.'||quote_ident(t.tablename), 'INSERT,UPDATE,DELETE,TRUNCATE'))
    FROM pg_roles r WHERE r.rolname = current_user
    """;
if (await check.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Read-only restricted database role required.");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
await scope.ServiceProvider.GetRequiredService<FiscalTrustMonitor>().RunCycleAsync(timeout.Token);
var registry = provider.GetRequiredService<FiscalTrustRegistry>();
var cache = provider.GetRequiredService<FiscalPreflightCache>();
var report = new FiscalPreflightReader(db, registry, cache);
var first = await report.ReadAsync(1, 100, timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(first));
for (var page = 2; page <= first.TotalPages; page++)
    Console.WriteLine(JsonSerializer.Serialize(await report.ReadAsync(page, 100, timeout.Token)));
