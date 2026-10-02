using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Pos.Backend.Api.Configuration;

public static class SecurityConfiguration
{
    public const string LoginPolicy = "AuthLogin";

    public static IServiceCollection AddSecurityConfiguration(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var validator = new SecurityOptionsValidation(environment);
        Add<JwtOptions>(services, configuration.GetSection("Jwt"), validator);
        Add<SriOptions>(services, configuration.GetSection("Sri"), validator);
        Add<CorsOptions>(services, configuration.GetSection("Cors"), validator);
        Add<DataProtectionSettings>(services, configuration.GetSection("DataProtection"), validator);
        Add<ReverseProxyOptions>(services, configuration.GetSection("ForwardedHeaders"), validator);
        Add<AuthRateLimitOptions>(services, configuration.GetSection("AuthRateLimit"), validator);
        Add<TransportOptions>(services, configuration.GetSection("Transport"), validator);
        Add<StartupSafetyOptions>(services, configuration, validator);
        Add<PlatformBootstrapOptions>(services, configuration.GetSection("PlatformBootstrap"), validator);

        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<CorsOptions>>((o, configured) => o.AddPolicy("AllowAngular", policy =>
            {
                var origins = configured.Value.AllowedOrigins.Select(value => value.TrimEnd('/')).ToArray();
                if (origins.Length > 0) policy.WithOrigins(origins);
                policy.AllowAnyHeader().AllowAnyMethod();
            }));

        var protection = configuration.GetSection("DataProtection").Get<DataProtectionSettings>() ?? new();
        var dataProtection = services.AddDataProtection();
        // Preserve the implicit Development discriminator/key ring unless a path is explicitly configured.
        if (!string.IsNullOrWhiteSpace(protection.KeysPath))
            dataProtection.SetApplicationName(protection.ApplicationName)
                .PersistKeysToFileSystem(new DirectoryInfo(protection.KeysPath));

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxyOptions>>((o, configured) =>
        {
            var proxy = configured.Value;
            if (!proxy.Enabled) return;
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = proxy.ForwardLimit;
            // Replace framework loopback defaults with a validated, non-empty explicit allowlist.
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var ip in proxy.KnownProxies) o.KnownProxies.Add(IPAddress.Parse(ip));
        });
        services.AddHsts(o => { o.Preload = false; o.IncludeSubDomains = false; });
        services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HstsOptions>()
            .Configure<IOptions<TransportOptions>>((o, configured) => o.MaxAge = TimeSpan.FromDays(configured.Value.HstsMaxAgeDays));

        services.AddRateLimiter(_ => { });
        services.AddOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>>((o, configured) =>
            {
                var limits = configured.Value;
                o.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                    $"{(context.Request.Path.StartsWithSegments("/api/platform", StringComparison.OrdinalIgnoreCase) ? "platform" : "tenant")}:{context.Connection.RemoteIpAddress}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit, Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0, AutoReplenishment = true
                    }));
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                o.OnRejected = async (context, cancellationToken) =>
                {
                    var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                        ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : limits.WindowSeconds;
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    await context.HttpContext.Response.WriteAsJsonAsync(new { error = "RATE_LIMITED" }, cancellationToken);
                };
            });
        return services;
    }

    private static void Add<T>(IServiceCollection services, IConfiguration section, IValidateOptions<T> validator) where T : class
    {
        services.AddSingleton(validator);
        services.AddOptions<T>().Bind(section).ValidateOnStart();
    }

    // Validate before any optional seed/bootstrap can touch a database, not just when RunAsync starts.
    public static void ValidateBeforeStartup(this IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptions<JwtOptions>>().Value;
        _ = services.GetRequiredService<IOptions<SriOptions>>().Value;
        _ = services.GetRequiredService<IOptions<CorsOptions>>().Value;
        _ = services.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
        _ = services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;
        _ = services.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
        _ = services.GetRequiredService<IOptions<TransportOptions>>().Value;
        _ = services.GetRequiredService<IOptions<StartupSafetyOptions>>().Value;
        _ = services.GetRequiredService<IOptions<PlatformBootstrapOptions>>().Value;
    }
}
