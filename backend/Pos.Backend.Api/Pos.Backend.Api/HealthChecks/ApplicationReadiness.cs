using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pos.Backend.Api.HealthChecks;

public sealed class ApplicationReadiness(IHostApplicationLifetime lifetime) : IHostedService, IHealthCheck, IDisposable
{
    private int _accepting;
    private CancellationTokenRegistration _started;
    private CancellationTokenRegistration _stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = lifetime.ApplicationStarted.Register(() => Volatile.Write(ref _accepting, 1));
        _stopping = lifetime.ApplicationStopping.Register(() => Volatile.Write(ref _accepting, 0));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    { Volatile.Write(ref _accepting, 0); return Task.CompletedTask; }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(Volatile.Read(ref _accepting) == 1 && !lifetime.ApplicationStopping.IsCancellationRequested
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());

    public void Dispose() { _started.Dispose(); _stopping.Dispose(); }
}
