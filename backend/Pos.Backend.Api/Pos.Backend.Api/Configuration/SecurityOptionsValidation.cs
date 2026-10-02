using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Configuration;

// Validation messages name settings, never their supplied values.
public sealed class SecurityOptionsValidation(IHostEnvironment environment) :
    IValidateOptions<JwtOptions>, IValidateOptions<CorsOptions>,
    IValidateOptions<DataProtectionSettings>, IValidateOptions<ReverseProxyOptions>,
    IValidateOptions<AuthRateLimitOptions>, IValidateOptions<TransportOptions>,
    IValidateOptions<StartupSafetyOptions>, IValidateOptions<PlatformBootstrapOptions>,
    IValidateOptions<SriOptions>
{
    private static ValidateOptionsResult Check(bool valid, string setting)
        => valid ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail($"Invalid configuration: {setting}.");

    public ValidateOptionsResult Validate(string? name, JwtOptions o) => Check(
        !string.IsNullOrWhiteSpace(o.Key) && Encoding.UTF8.GetByteCount(o.Key) >= 32
        && !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience)
        && o.ExpiresMinutes is >= 5 and <= 1440 && o.ClockSkewSeconds is >= 0 and <= 120, "Jwt");

    public ValidateOptionsResult Validate(string? name, CorsOptions o) => Check(
        o.AllowedOrigins.All(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Host.Length > 0 && !origin.Contains('*') && uri.UserInfo.Length == 0
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && (uri.Scheme == "https" || (!environment.IsProduction() && uri.Scheme == "http"))), "Cors:AllowedOrigins");

    public ValidateOptionsResult Validate(string? name, DataProtectionSettings o)
    {
        if (!environment.IsProduction() && string.IsNullOrWhiteSpace(o.KeysPath))
            return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(o.ApplicationName) || !Path.IsPathFullyQualified(o.KeysPath)
            || !Directory.Exists(o.KeysPath))
            return ValidateOptionsResult.Fail("Invalid configuration: DataProtection.");
        try
        {
            // A temporary probe verifies read/write access, without creating a missing mount.
            using var probe = new FileStream(Path.Combine(o.KeysPath, $".hfpos-probe-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return ValidateOptionsResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValidateOptionsResult.Fail("DataProtection:KeysPath must be readable and writable.");
        }
    }

    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions o) => Check(
        o.ForwardLimit is >= 1 and <= 3 && o.KnownProxies.All(ip => IPAddress.TryParse(ip, out _))
        && (!o.Enabled || o.KnownProxies.Length > 0), "ForwardedHeaders");

    public ValidateOptionsResult Validate(string? name, AuthRateLimitOptions o) => Check(
        o.PermitLimit is >= 1 and <= 100 && o.WindowSeconds is >= 1 and <= 3600, "AuthRateLimit");

    public ValidateOptionsResult Validate(string? name, TransportOptions o) => Check(
        o.HstsMaxAgeDays is >= 1 and <= 365, "Transport:HstsMaxAgeDays");

    public ValidateOptionsResult Validate(string? name, StartupSafetyOptions o) => Check(
        !o.AutomaticForwardedHeadersEnabled && !string.IsNullOrWhiteSpace(o.ConnectionStrings.DefaultConnection)
        && (!environment.IsProduction() || (!o.SeedDemoData && !string.IsNullOrWhiteSpace(o.AllowedHosts)
            && o.AllowedHosts.Split(';').All(host => !string.IsNullOrWhiteSpace(host)
                && !host.Contains('*') && Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown))),
        "ConnectionStrings:DefaultConnection / AllowedHosts / SeedDemoData / ForwardedHeaders_Enabled");

    public ValidateOptionsResult Validate(string? name, PlatformBootstrapOptions o) => Check(
        !o.Enabled || PlatformAuthService.ValidIdentity(o.Username.Trim(), o.Email.Trim(), o.Password), "PlatformBootstrap");

    public ValidateOptionsResult Validate(string? name, SriOptions o) => Check(
        o.Environment is 1 or 2 && o.TimeoutSeconds is >= 5 and <= 120
        && (string.IsNullOrWhiteSpace(o.ReceptionTestUrl) || Https(o.ReceptionTestUrl))
        && (string.IsNullOrWhiteSpace(o.AuthorizationTestUrl) || Https(o.AuthorizationTestUrl))
        && (string.IsNullOrWhiteSpace(o.ReceptionProductionUrl) || Https(o.ReceptionProductionUrl))
        && (string.IsNullOrWhiteSpace(o.AuthorizationProductionUrl) || Https(o.AuthorizationProductionUrl))
        && (o.Environment != 1 || (Https(o.ReceptionTestUrl) && Https(o.AuthorizationTestUrl)))
        && (!o.AllowProductionSubmission || (Https(o.ReceptionProductionUrl) && Https(o.AuthorizationProductionUrl)
            && !SameEndpoint(o.ReceptionProductionUrl, o.ReceptionTestUrl)
            && !SameEndpoint(o.ReceptionProductionUrl, o.AuthorizationTestUrl)
            && !SameEndpoint(o.AuthorizationProductionUrl, o.ReceptionTestUrl)
            && !SameEndpoint(o.AuthorizationProductionUrl, o.AuthorizationTestUrl))), "Sri");

    private static bool Https(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    private static bool SameEndpoint(string? left, string? right)
        => Uri.TryCreate(left, UriKind.Absolute, out var a) && Uri.TryCreate(right, UriKind.Absolute, out var b) && a == b;
}
