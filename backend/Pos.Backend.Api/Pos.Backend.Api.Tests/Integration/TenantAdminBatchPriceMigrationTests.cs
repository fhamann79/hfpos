using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class TenantAdminBatchPriceMigrationTests(PostgresDatabaseFixture database)
{
    internal const string MigrationId = "20261119120000_ReconcileTenantAdminBatchPricePermission";
    private const string Batch = AppPermissions.CatalogProductPricesBatch;
    private const string Custom = "SYNTHETIC_CUSTOM_PERMISSION";
    private static readonly string[] FiscalBatchCodes =
    [
        AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite,
        AppPermissions.FiscalSettingsRead, AppPermissions.FiscalSettingsWrite
    ];
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconciles_only_tenant_admins_and_preserves_unrelated_data_on_retry(bool catalogExists)
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "batch-migration", 53201, 1m);
        await using var db = database.CreateDbContext();
        var first = await db.Roles.SingleAsync(r => r.CompanyId == tenant.CompanyId);
        first.Code = AppRoles.Admin;
        first.AuthorizationVersion = 7;
        var user = await db.Users.SingleAsync();
        user.SessionVersion = 11;
        var inactiveAdmin = AddRole(db, AddCompany(db, 53202), AppRoles.Admin, 15, active: false);
        var suspended = AddCompany(db, 53203, active: false);
        var suspendedAdmin = AddRole(db, suspended, AppRoles.Admin, 21);
        var custom = AddRole(db, AddCompany(db, 53204), "CUSTOM", 30);
        var supervisor = AddRole(db, suspended, AppRoles.Supervisor, 32);
        var cashier = AddRole(db, suspended, AppRoles.Cashier, 33);
        var customPermission = AddPermission(db, Custom);
        db.RolePermissions.Add(new() { Role = custom, Permission = customPermission });
        db.RolePermissions.Add(new() { Role = first, Permission = customPermission });
        foreach (var code in FiscalBatchCodes)
        {
            var permission = AddPermission(db, code, active: code != AppPermissions.FiscalSettingsWrite);
            db.RolePermissions.Add(new() { Role = first, Permission = permission });
            if (code == AppPermissions.FiscalSettingsRead)
                db.RolePermissions.Add(new() { Role = supervisor, Permission = permission });
        }
        var sri = AddPermission(db, AppPermissions.SriDocumentsSign);
        db.RolePermissions.Add(new() { Role = custom, Permission = sri });
        if (catalogExists)
            db.RolePermissions.Add(new() { Role = first, Permission = AddPermission(db, Batch) });
        await db.SaveChangesAsync();

        var existingPermission = await db.Permissions.AsNoTracking().SingleOrDefaultAsync(p => p.Code == Batch);
        var unrelatedGrants = await OtherGrantsAsync(db);
        var unrelatedCatalog = await OtherCatalogAsync(db);
        await db.Database.OpenConnectionAsync();
        var reports = CaptureReports(db);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();

        var permissionAfter = await db.Permissions.AsNoTracking().SingleAsync(p => p.Code == Batch);
        Assert.True(permissionAfter.IsActive);
        if (existingPermission is not null)
        {
            Assert.Equal((existingPermission.Id, existingPermission.Description, existingPermission.CreatedAt),
                (permissionAfter.Id, permissionAfter.Description, permissionAfter.CreatedAt));
        }
        else
        {
            Assert.Equal(TenantDefaults.Permissions.Single(p => p.Code == Batch).Description, permissionAfter.Description);
        }
        Assert.Equal(new[] { first.Id, inactiveAdmin.Id, suspendedAdmin.Id }.Order(), await BatchRolesAsync(db));
        var expectedVersions = new Dictionary<int, long>
        {
            [first.Id] = catalogExists ? 7 : 8, [inactiveAdmin.Id] = 16, [suspendedAdmin.Id] = 22,
            [custom.Id] = 30, [supervisor.Id] = 32, [cashier.Id] = 33
        };
        await AssertVersionsAsync(db, expectedVersions);
        Assert.Equal(unrelatedGrants, await OtherGrantsAsync(db));
        Assert.Equal(unrelatedCatalog, await OtherCatalogAsync(db));
        Assert.Equal(11, await db.Users.Select(u => u.SessionVersion).SingleAsync());
        Assert.False(await db.Roles.Where(r => r.Id == inactiveAdmin.Id).Select(r => r.IsActive).SingleAsync());
        Assert.False(await db.Companies.Where(c => c.Id == suspended.Id).Select(c => c.IsActive).SingleAsync());
        Assert.Equal($"DEV532_RBAC companies=4 companies_without_admin=1 admins=3 batch_catalog_added={(catalogExists ? 0 : 1)} "
            + $"new_grants={(catalogExists ? 2 : 3)} fiscal_status=REVIEW_REQUIRED fiscal_catalog_missing=0 "
            + "fiscal_catalog_inactive=1 fiscal_admin_missing_grants=9", Assert.Single(reports));

        // A lost history row simulates retrying the actual Up SQL against already reconciled data.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = {MigrationId}""");
        await migrator.MigrateAsync();
        Assert.Contains("batch_catalog_added=0 new_grants=0", reports[1]);
        await AssertVersionsAsync(db, expectedVersions);
        Assert.Equal(new[] { first.Id, inactiveAdmin.Id, suspendedAdmin.Id }.Order(), await BatchRolesAsync(db));

        await db.Roles.Where(r => r.Id == inactiveAdmin.Id).ExecuteUpdateAsync(r => r.SetProperty(x => x.IsActive, true));
        await db.Companies.Where(c => c.Id == suspended.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.IsActive, true));
        Assert.Equal(3, await db.RolePermissions.CountAsync(rp =>
            rp.Permission.Code == Batch && rp.Role.IsActive && rp.Role.Company.IsActive));

        var down = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(PostgresDatabaseFixture.PreDev532Migration));
        Assert.Equal("DEV532_RBAC_DOWNGRADE_BLOCKED", down.MessageText);
        Assert.Contains(MigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(3, (await BatchRolesAsync(db)).Length);
        await AssertVersionsAsync(db, expectedVersions);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("inactive", "DEV532_BATCH_PERMISSION_INACTIVE")]
    [InlineData("non-admin", "DEV532_BATCH_PERMISSION_UNEXPECTED_GRANT")]
    [InlineData("orphan", "DEV532_ADMIN_ROLE_INCONSISTENT")]
    public async Task Rejects_inconsistent_state_without_deleting_existing_data(string anomaly, string expectedError)
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var db = database.CreateDbContext();
        var company = AddCompany(db, 53205);
        var admin = AddRole(db, company, AppRoles.Admin, 6);
        var custom = AddRole(db, company, "CUSTOM", 8, active: false);
        var permission = AddPermission(db, Batch, active: anomaly != "inactive");
        if (anomaly == "non-admin")
            db.RolePermissions.Add(new() { Role = custom, Permission = permission });
        await db.SaveChangesAsync();
        if (anomaly == "orphan")
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Roles" DROP CONSTRAINT "FK_Roles_Companies_CompanyId" """);
            await db.Roles.Where(r => r.Id == admin.Id).ExecuteUpdateAsync(r => r.SetProperty(x => x.CompanyId, 999));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync());
        Assert.Equal(expectedError, exception.MessageText);
        Assert.DoesNotContain(MigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(anomaly == "non-admin" ? new[] { custom.Id } : [], await BatchRolesAsync(db));
        await AssertVersionsAsync(db, new() { [admin.Id] = 6, [custom.Id] = 8 });
        Assert.Equal(permission.IsActive, await db.Permissions.Where(p => p.Code == Batch).Select(p => p.IsActive).SingleAsync());
    }

    [Fact]
    public async Task Version_overflow_rolls_back_catalog_grants_and_history_and_allows_clean_retry()
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var db = database.CreateDbContext();
        var one = AddRole(db, AddCompany(db, 53206), AppRoles.Admin, 4);
        var two = AddRole(db, AddCompany(db, 53207), AppRoles.Admin, long.MaxValue);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync());
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, exception.SqlState);
        Assert.DoesNotContain(MigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.False(await db.Permissions.AnyAsync(p => p.Code == Batch));
        Assert.Empty(await db.RolePermissions.ToListAsync());
        await AssertVersionsAsync(db, new() { [one.Id] = 4, [two.Id] = long.MaxValue });

        await db.Roles.Where(r => r.Id == two.Id).ExecuteUpdateAsync(r => r.SetProperty(x => x.AuthorizationVersion, 9L));
        await db.GetService<IMigrator>().MigrateAsync();
        await AssertVersionsAsync(db, new() { [one.Id] = 5, [two.Id] = 10 });
        Assert.Equal(2, (await BatchRolesAsync(db)).Length);
    }

    [Fact]
    public async Task Clean_database_without_fiscal_catalog_migrates_before_seeding()
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var db = database.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        var reports = CaptureReports(db);
        await db.GetService<IMigrator>().MigrateAsync();
        Assert.True(await db.Permissions.AnyAsync(p => p.Code == Batch && p.IsActive));
        Assert.Empty(await db.RolePermissions.ToListAsync());
        Assert.Contains("fiscal_status=EMPTY_PRE_SEED fiscal_catalog_missing=4", Assert.Single(reports));
        await SeedData.SeedDevelopmentAsync(db);
        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Code == Batch));
        Assert.Contains(AppRoles.Admin, await db.RolePermissions
            .Where(rp => rp.Permission.Code == Batch).Select(rp => rp.Role.Code).ToListAsync());
    }

    [Fact]
    public async Task Fiscal_diagnostic_ignores_sri_and_legitimate_partial_non_admin_grants()
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var db = database.CreateDbContext();
        var company = AddCompany(db, 53213);
        var admin = AddRole(db, company, AppRoles.Admin, 2);
        var staff = AddRole(db, company, AppRoles.Supervisor, 3);
        foreach (var code in FiscalBatchCodes)
        {
            var permission = AddPermission(db, code);
            db.RolePermissions.Add(new() { Role = admin, Permission = permission });
            if (code is AppPermissions.CatalogProductsRead or AppPermissions.FiscalSettingsRead)
                db.RolePermissions.Add(new() { Role = staff, Permission = permission });
        }
        await db.SaveChangesAsync();
        var original = await OtherGrantsAsync(db);
        await db.Database.OpenConnectionAsync();
        var reports = CaptureReports(db);
        await db.GetService<IMigrator>().MigrateAsync();
        Assert.Contains("fiscal_status=OK fiscal_catalog_missing=0 fiscal_catalog_inactive=0 fiscal_admin_missing_grants=0",
            Assert.Single(reports));
        Assert.Equal(original, await OtherGrantsAsync(db));
    }

    [Fact]
    public async Task Waits_for_tenant_administration_and_increments_its_committed_version()
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var admin = database.CreateDbContext();
        var role = AddRole(admin, AddCompany(admin, 53208), AppRoles.Admin, 18);
        var permission = AddPermission(admin, Custom);
        await admin.SaveChangesAsync();
        await admin.Database.OpenConnectionAsync();
        await using var tx = await new TenantAdministrationGuard(admin).BeginChangeAsync(role.CompanyId);
        admin.RolePermissions.Add(new() { Role = role, Permission = permission });
        role.AuthorizationVersion++;
        await admin.SaveChangesAsync();

        await using var migratorDb = database.CreateDbContext();
        await migratorDb.Database.OpenConnectionAsync();
        var pending = migratorDb.GetService<IMigrator>().MigrateAsync();
        try
        {
            await using var observer = database.CreateDbContext();
            await WaitForBlockAsync(observer, migratorDb, admin);
            Assert.Equal(18, await observer.Roles.Select(r => r.AuthorizationVersion).SingleAsync());
        }
        finally
        {
            await tx.CommitAsync();
            await pending;
        }
        await AssertVersionsAsync(migratorDb, new() { [role.Id] = 20 });
        Assert.Equal(new[] { role.Id }, await BatchRolesAsync(migratorDb));
        Assert.Single(await OtherGrantsAsync(migratorDb));
    }

    [Fact]
    public async Task Conflicting_company_lock_times_out_without_partial_changes()
    {
        await using var baseline = await database.UseMigrationBaselineAsync(PostgresDatabaseFixture.PreDev532Migration);
        await using var admin = database.CreateDbContext();
        var role = AddRole(admin, AddCompany(admin, 53209), AppRoles.Admin, 3);
        await admin.SaveChangesAsync();
        await using var tx = await new TenantAdministrationGuard(admin).BeginChangeAsync(role.CompanyId);
        await using var db = database.CreateDbContext();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync());
        var exception = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        Assert.DoesNotContain(MigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.False(await db.Permissions.AnyAsync(p => p.Code == Batch));
        await AssertVersionsAsync(db, new() { [role.Id] = 3 });
    }

    internal static async Task WaitForBlockAsync(PosDbContext observer, PosDbContext waiter, PosDbContext blocker)
    {
        var waiterPid = ((NpgsqlConnection)waiter.Database.GetDbConnection()).ProcessID;
        var blockerPid = ((NpgsqlConnection)blocker.Database.GetDbConnection()).ProcessID;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await observer.Database.OpenConnectionAsync(deadline.Token);
        try
        {
            while (!await observer.Database.SqlQuery<bool>(
                $"""SELECT {blockerPid} = ANY(pg_blocking_pids({waiterPid})) AS "Value" """)
                .SingleAsync(deadline.Token))
                await Task.Yield();
        }
        finally { await observer.Database.CloseConnectionAsync(); }
    }

    private static List<string> CaptureReports(PosDbContext db)
    {
        var reports = new List<string>();
        ((NpgsqlConnection)db.Database.GetDbConnection()).Notice += (_, e) =>
        {
            if (e.Notice.MessageText.StartsWith("DEV532_RBAC ", StringComparison.Ordinal))
                reports.Add(e.Notice.MessageText);
        };
        return reports;
    }

    private static Company AddCompany(PosDbContext db, int seed, bool active = true) =>
        db.Companies.Add(new() { Name = "Synthetic tenant", Ruc = $"9{seed:D12}", IsActive = active, CreatedAt = Now }).Entity;
    private static Role AddRole(PosDbContext db, Company company, string code, long version, bool active = true) =>
        db.Roles.Add(new() { Company = company, Code = code, Name = code, IsActive = active, AuthorizationVersion = version, CreatedAt = Now }).Entity;
    private static Permission AddPermission(PosDbContext db, string code, bool active = true) =>
        db.Permissions.Add(new() { Code = code, Description = "Synthetic existing description", IsActive = active, CreatedAt = Now }).Entity;
    private static Task<int[]> BatchRolesAsync(PosDbContext db) =>
        db.RolePermissions.Where(rp => rp.Permission.Code == Batch).OrderBy(rp => rp.RoleId).Select(rp => rp.RoleId).ToArrayAsync();
    private static Task<int[][]> OtherGrantsAsync(PosDbContext db) =>
        db.RolePermissions.Where(rp => rp.Permission.Code != Batch).OrderBy(rp => rp.RoleId).ThenBy(rp => rp.PermissionId)
            .Select(rp => new[] { rp.RoleId, rp.PermissionId }).ToArrayAsync();
    private static Task<string[]> OtherCatalogAsync(PosDbContext db) =>
        db.Permissions.Where(p => p.Code != Batch).OrderBy(p => p.Id)
            .Select(p => p.Code + ":" + p.Description + ":" + p.IsActive).ToArrayAsync();
    private static async Task AssertVersionsAsync(PosDbContext db, Dictionary<int, long> expected)
    {
        var actual = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.AuthorizationVersion);
        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }
}
