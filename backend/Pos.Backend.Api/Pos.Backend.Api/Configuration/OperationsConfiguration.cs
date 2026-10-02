using System.Reflection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pos.Backend.Api.HealthChecks;

namespace Pos.Backend.Api.Configuration;

public sealed class OperationsOptions
{
    public int ShutdownTimeoutSeconds { get; set; } = 30;
}

public sealed class ObservabilityOptions
{
    public bool Enabled { get; set; }
    public string ServiceName { get; set; } = "hfpos-api";
    public string OtlpEndpoint { get; set; } = "";
    public double TraceSampleRatio { get; set; } = 0.1;
}

public static class OperationsConfiguration
{
    public static string ReleaseVersion => Environment.GetEnvironmentVariable("HFPOS_RELEASE_VERSION")
        ?? typeof(OperationsConfiguration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IServiceCollection AddOperations(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<OperationsOptions>().Bind(configuration.GetSection("Operations"))
            .Validate(o => o.ShutdownTimeoutSeconds is >= 5 and <= 120, "Operations shutdown timeout must be 5..120 seconds.")
            .ValidateOnStart();
        services.AddOptions<ObservabilityOptions>().Bind(configuration.GetSection("Observability"))
            .Validate(IsValid, "Invalid observability configuration (service, ratio or OTLP endpoint).")
            .ValidateOnStart();
        services.AddOptions<HostOptions>().Configure<IOptions<OperationsOptions>>((host, operations) =>
            host.ShutdownTimeout = TimeSpan.FromSeconds(operations.Value.ShutdownTimeoutSeconds));
        services.AddSingleton<ApplicationReadiness>();
        services.AddHostedService(provider => provider.GetRequiredService<ApplicationReadiness>());

        // Register instrumentation now; construct optional exporters only after validated DI options are available.
        services.AddOpenTelemetry().WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(o => { o.RecordException = false; o.Filter = c => !IsProbe(c.Request.Path); })
            .AddHttpClientInstrumentation(o => o.RecordException = false)
            .AddProcessor(new SafeHttpTraceProcessor())
            .AddProcessor(provider =>
            {
                var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                return options.Enabled
                    ? new BatchActivityExportProcessor(new OtlpTraceExporter(Exporter(options, "traces")), exporterTimeoutMilliseconds: 3000)
                    : new DisabledTraceProcessor();
            }))
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
                .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
                { TagKeys = ["http.request.method", "http.response.status_code", "http.route"] })
                .AddView("http.client.request.duration", new ExplicitBucketHistogramConfiguration
                { TagKeys = ["http.request.method", "http.response.status_code"] })
                .AddReader(provider =>
                {
                    var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
                    return options.Enabled
                        ? new PeriodicExportingMetricReader(new OtlpMetricExporter(Exporter(options, "metrics")), exportTimeoutMilliseconds: 3000)
                        : new DisabledMetricReader();
                }));
        services.ConfigureOpenTelemetryTracerProvider((provider, tracing) =>
        {
            var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
            tracing.SetResourceBuilder(Resource(options, environment.EnvironmentName))
                .SetSampler(options.Enabled ? new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio)) : new AlwaysOffSampler());
        });
        services.ConfigureOpenTelemetryMeterProvider((provider, metrics) =>
            metrics.SetResourceBuilder(Resource(provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value, environment.EnvironmentName)));
        return services;
    }

    public static bool IsValid(ObservabilityOptions options) => double.IsFinite(options.TraceSampleRatio)
        && options.TraceSampleRatio is >= 0 and <= 1
        && (!options.Enabled || (!string.IsNullOrWhiteSpace(options.ServiceName)
            && Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)));

    public static ResourceBuilder Resource(ObservabilityOptions options, string environment) => ResourceBuilder.CreateEmpty()
        .AddService(string.IsNullOrWhiteSpace(options.ServiceName) ? "hfpos-api" : options.ServiceName, serviceVersion: ReleaseVersion)
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environment)]);

    public static bool IsProbe(PathString path) => path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);

    private static OtlpExporterOptions Exporter(ObservabilityOptions options, string signal)
    {
        return new OtlpExporterOptions
        {
            Endpoint = new Uri($"{options.OtlpEndpoint.TrimEnd('/')}/v1/{signal}"),
            Protocol = OtlpExportProtocol.HttpProtobuf,
            TimeoutMilliseconds = 3000,
            Headers = null
        };
    }

    private sealed class DisabledTraceProcessor : BaseProcessor<System.Diagnostics.Activity> { }
    private sealed class DisabledMetricReader : MetricReader { }
}

// Use only low-cardinality HTTP metadata; never export URL/query/headers or exception payloads.
public sealed class SafeHttpTraceProcessor : BaseProcessor<System.Diagnostics.Activity>
{
    private static readonly HashSet<string> Allowed = ["http.request.method", "http.response.status_code", "http.route", "network.protocol.version"];
    public override void OnEnd(System.Diagnostics.Activity activity)
    {
        activity.TraceStateString = null;
        if (activity.Status == System.Diagnostics.ActivityStatusCode.Error)
            activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
        foreach (var tag in activity.TagObjects.ToArray())
            if (!Allowed.Contains(tag.Key)) activity.SetTag(tag.Key, null);
        activity.DisplayName = $"HTTP {activity.GetTagItem("http.request.method")} {activity.GetTagItem("http.route")}".TrimEnd();
    }
}
