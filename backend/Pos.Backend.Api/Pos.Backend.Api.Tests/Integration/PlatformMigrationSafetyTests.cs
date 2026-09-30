using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PlatformMigrationSafetyTests(PostgresDatabaseFixture database)
{
    [Theory]
    [InlineData("Companies", "Ruc")]
    [InlineData("Users", "Username")]
    [InlineData("Users", "Email")]
    public async Task Duplicate_legacy_rows_abort_without_automatic_data_repair(string table, string column)
    {
        try
        {
            await database.RecreateDatabaseAsync();
            await using var context = database.CreateDbContext();
            await context.GetService<IMigrator>().MigrateAsync("20260930001830_AddPaymentSettlements");
            var one = await TestDataBuilder.CreateTenantAsync(database, "duplicate-one", 330, 1m);
            var two = await TestDataBuilder.CreateTenantAsync(database, "duplicate-two", 331, 1m);
            var oneId = table == "Companies" ? one.CompanyId : one.UserId;
            var twoId = table == "Companies" ? two.CompanyId : two.UserId;
            var updateSql = (table, column) switch
            {
                ("Companies", "Ruc") => "UPDATE \"Companies\" SET \"Ruc\" = (SELECT \"Ruc\" FROM \"Companies\" WHERE \"Id\" = {0}) WHERE \"Id\" = {1}",
                ("Users", "Username") => "UPDATE \"Users\" SET \"Username\" = (SELECT \"Username\" FROM \"Users\" WHERE \"Id\" = {0}) WHERE \"Id\" = {1}",
                ("Users", "Email") => "UPDATE \"Users\" SET \"Email\" = (SELECT \"Email\" FROM \"Users\" WHERE \"Id\" = {0}) WHERE \"Id\" = {1}",
                _ => throw new ArgumentException("Unsupported test column")
            };
            await context.Database.ExecuteSqlRawAsync(updateSql, oneId, twoId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync());
            Assert.Contains($"duplicate {table}.{column}", error.MessageText);
            Assert.Equal(2, await context.Companies.CountAsync());
            Assert.Equal(2, await context.Users.CountAsync());
            var duplicateSql = (table, column) switch
            {
                ("Companies", "Ruc") => "SELECT COUNT(*)::integer AS \"Value\" FROM (SELECT \"Ruc\" FROM \"Companies\" GROUP BY \"Ruc\" HAVING COUNT(*) > 1) d",
                ("Users", "Username") => "SELECT COUNT(*)::integer AS \"Value\" FROM (SELECT \"Username\" FROM \"Users\" GROUP BY \"Username\" HAVING COUNT(*) > 1) d",
                ("Users", "Email") => "SELECT COUNT(*)::integer AS \"Value\" FROM (SELECT \"Email\" FROM \"Users\" GROUP BY \"Email\" HAVING COUNT(*) > 1) d",
                _ => throw new ArgumentException("Unsupported test column")
            };
            var duplicates = await context.Database.SqlQueryRaw<int>(duplicateSql).SingleAsync();
            Assert.Equal(1, duplicates);
        }
        finally { await database.RecreateDatabaseAsync(); }
    }
}
