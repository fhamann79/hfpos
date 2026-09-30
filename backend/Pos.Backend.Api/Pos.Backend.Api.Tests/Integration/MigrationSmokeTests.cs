using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class MigrationSmokeTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Clean_database_applies_every_migration_and_matches_the_model()
    {
        await database.RecreateDatabaseAsync();

        await using var context = database.CreateDbContext();
        var definedMigrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToArray();

        Assert.NotEmpty(definedMigrations);
        Assert.Equal(definedMigrations, appliedMigrations);
        Assert.Empty(pendingMigrations);
        Assert.Contains(definedMigrations, migration => migration.EndsWith("AddSaleVoidCashSemantics"));
        Assert.Contains(definedMigrations, migration => migration.EndsWith("AddSessionAuthorizationVersions"));
        Assert.Contains(definedMigrations, migration => migration.EndsWith("AddInventoryTransfers"));
        Assert.Contains(definedMigrations, migration => migration.EndsWith("AddPaymentSettlements"));
        Assert.Contains(definedMigrations, migration => migration.EndsWith("AddSaasPlatformControlPlane"));
        Assert.Empty(await context.PlatformUsers.ToListAsync());
        Assert.Empty(await context.PlatformTenantEvents.ToListAsync());
        foreach (var index in new[] { "IX_Companies_Ruc", "IX_Users_Username", "IX_Users_Email", "IX_PlatformUsers_Username", "IX_PlatformUsers_Email" })
        {
            var unique = await context.Database.SqlQuery<int>($"SELECT COUNT(*)::integer AS \"Value\" FROM pg_indexes WHERE indexname = {index} AND indexdef LIKE 'CREATE UNIQUE%'").SingleAsync();
            Assert.Equal(1, unique);
        }
        Assert.Equal(0, await context.Companies.CountAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}

public sealed class TestDatabaseSafetyTests
{
    [Fact]
    public void Destructive_setup_rejects_non_test_database_names()
    {
        foreach (var databaseName in new[] { "postgres", "hfpos", "hfpos_development", "production" })
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => PostgresDatabaseFixture.EnsureSafeDatabaseName(databaseName));

            Assert.Contains("not an explicit hfpos test/CI database", exception.Message);
        }
    }

    [Fact]
    public void Destructive_setup_accepts_only_explicit_test_database_names()
    {
        PostgresDatabaseFixture.EnsureSafeDatabaseName("hfpos_test");
        PostgresDatabaseFixture.EnsureSafeDatabaseName("hfpos_test_local_01");
        PostgresDatabaseFixture.EnsureSafeDatabaseName("hfpos_ci");
        PostgresDatabaseFixture.EnsureSafeDatabaseName("hfpos_ci_pr_74");
    }
}
