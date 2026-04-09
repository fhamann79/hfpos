using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.HealthChecks;

public class PostgresReadinessHealthCheck : IHealthCheck
{
    private readonly PosDbContext _dbContext;

    public PostgresReadinessHealthCheck(PosDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
    }
}
