using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class MigrationSmokeTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Compatible_populated_initial_schema_preserves_tenants_through_every_migration()
    {
        try
        {
            await using var context = database.CreateDbContext();
            await context.Database.EnsureDeletedAsync();
            await context.GetService<IMigrator>().MigrateAsync("20251216025500_InitialSchema");
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Companies" ("Name","Ruc","CreatedAt") VALUES
                    ('SYNTHETIC LEGACY A','9000000000531',now()), ('SYNTHETIC LEGACY B','9000000000532',now());
                INSERT INTO "Users" ("Username","Email","PasswordHash","IsActive","CreatedAt","CompanyId")
                    SELECT 'legacy-' || "Id", 'legacy-' || "Id" || '@example.invalid', 'synthetic-hash-only', true, now(), "Id" FROM "Companies";
                """);
            foreach (var migration in context.Database.GetMigrations().Skip(1))
            {
                await context.GetService<IMigrator>().MigrateAsync(migration);
                if (migration.EndsWith("AddUserEstablishment"))
                    await context.Database.ExecuteSqlRawAsync("""
                        INSERT INTO "Establishments" ("CompanyId","Code","Name","Address","IsActive","CreatedAt")
                            SELECT "Id", '001', 'SYNTHETIC LEGACY', 'Synthetic address', true, now() FROM "Companies";
                        UPDATE "Users" u SET "EstablishmentId" = e."Id" FROM "Establishments" e WHERE e."CompanyId" = u."CompanyId";
                        """);
            }
            var companies = await context.Companies.OrderBy(c => c.Id).ToListAsync();
            Assert.Equal(new[] { "SYNTHETIC LEGACY A", "SYNTHETIC LEGACY B" }, companies.Select(c => c.Name));
            var users = await context.Users.OrderBy(u => u.CompanyId).ToListAsync();
            Assert.Equal(companies.Select(c => c.Id), users.Select(u => u.CompanyId));
            Assert.All(users, user => Assert.Equal("synthetic-hash-only", user.PasswordHash));
            Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
        }
        finally { await database.RecreateDatabaseAsync(); }
    }

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
