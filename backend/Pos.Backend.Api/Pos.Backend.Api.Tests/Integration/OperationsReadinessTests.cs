using System.Net;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class OperationsReadinessTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Database_readiness_remains_healthy_with_unreachable_local_collector()
    {
        using var factory = new ProductionSecurityApiFactory(new()
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["Observability:Enabled"] = "true",
            ["Observability:OtlpEndpoint"] = "http://127.0.0.1:1"
        });
        using var client = factory.Client();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Unhealthy", await response.Content.ReadAsStringAsync());
    }
}
