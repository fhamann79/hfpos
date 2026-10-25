namespace Pos.Backend.Api.Configuration;

public sealed class FiscalTrustOptions
{
    public string RegistryPath { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public int MonitorBatchSize { get; set; } = 20;
    public int MonitorIntervalSeconds { get; set; } = 30;
    public int MaximumCachedTenants { get; set; } = 10_000;
}
