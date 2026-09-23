using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Tests.Infrastructure;

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string ConnectionStringEnvironmentVariable = "HF_POS_TEST_CONNECTION_STRING";

    private static readonly Regex SafeDatabaseNamePattern = new(
        @"^hfpos_(test|ci)(_[a-z0-9_]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _connectionString;

    internal string ConnectionString => _connectionString;

    public PostgresDatabaseFixture()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} is required for PostgreSQL integration tests.");
        }

        var builder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
        {
            ApplicationName = "hfpos-backend-tests",
            IncludeErrorDetail = true
        };

        EnsureSafeDatabaseName(builder.Database);
        _connectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await RecreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await ResetDataAsync();
    }

    public PosDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseNpgsql(_connectionString)
            .EnableDetailedErrors();

        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new PosDbContext(options.Options);
    }

    public async Task RecreateDatabaseAsync()
    {
        EnsureSafeDatabaseName(new NpgsqlConnectionStringBuilder(_connectionString).Database);

        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task ResetDataAsync()
    {
        EnsureSafeDatabaseName(new NpgsqlConnectionStringBuilder(_connectionString).Database);

        const string resetSql = """
            DO $$
            DECLARE
                table_list text;
            BEGIN
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                INTO table_list
                FROM pg_tables
                WHERE schemaname = 'public'
                  AND tablename <> '__EFMigrationsHistory';

                IF table_list IS NOT NULL THEN
                    EXECUTE 'TRUNCATE TABLE ' || table_list || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(resetSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    internal static void EnsureSafeDatabaseName(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName)
            || !SafeDatabaseNamePattern.IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                "Refusing destructive test setup because the database name is not an explicit hfpos test/CI database.");
        }
    }
}
