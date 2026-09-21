namespace Pos.Backend.Api.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "PostgreSQL integration";
}
