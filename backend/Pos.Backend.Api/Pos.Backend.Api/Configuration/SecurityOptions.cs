namespace Pos.Backend.Api.Configuration;

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class DataProtectionSettings
{
    public string ApplicationName { get; set; } = "";
    public string KeysPath { get; set; } = "";
}

public sealed class ReverseProxyOptions
{
    public bool Enabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
    public int ForwardLimit { get; set; } = 1;
}

public sealed class AuthRateLimitOptions
{
    public int PermitLimit { get; set; } = 20;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class TransportOptions
{
    public int HstsMaxAgeDays { get; set; } = 30;
}

public sealed class StartupSafetyOptions
{
    public ConnectionSettings ConnectionStrings { get; set; } = new();
    public string AllowedHosts { get; set; } = "";
    public bool SeedDemoData { get; set; }
    [Microsoft.Extensions.Configuration.ConfigurationKeyName("ForwardedHeaders_Enabled")]
    public bool AutomaticForwardedHeadersEnabled { get; set; }
}

public sealed class ConnectionSettings
{
    public string DefaultConnection { get; set; } = "";
}
