using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.HealthChecks;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Middleware;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class OperationsTests
{
    [Theory]
    [InlineData("Operations:ShutdownTimeoutSeconds", "4")]
    [InlineData("Operations:ShutdownTimeoutSeconds", "121")]
    [InlineData("Observability:TraceSampleRatio", "-0.1")]
    [InlineData("Observability:TraceSampleRatio", "1.1")]
    [InlineData("Observability:TraceSampleRatio", "NaN")]
    public void Startup_rejects_invalid_operational_options(string key, string value)
    {
        using var factory = new ProductionSecurityApiFactory(new() { [key] = value });
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    [Theory]
    [InlineData("", "hfpos-api")]
    [InlineData("http://user:secret@localhost:4318", "hfpos-api")]
    [InlineData("http://localhost:4318?password=secret", "hfpos-api")]
    [InlineData("file:///tmp/telemetry", "hfpos-api")]
    [InlineData("http://localhost:4318", "")]
    public void Enabled_telemetry_requires_a_safe_endpoint_and_service(string endpoint, string service)
    {
        using var factory = new ProductionSecurityApiFactory(new()
        { ["Observability:Enabled"] = "true", ["Observability:OtlpEndpoint"] = endpoint, ["Observability:ServiceName"] = service });
        Assert.Throws<OptionsValidationException>(() => factory.Client());
    }

    [Fact]
    public async Task Disabled_telemetry_and_internal_liveness_work_without_database()
    {
        using var factory = new ProductionSecurityApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://example.test"), AllowAutoRedirect = false });
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
        Assert.False(factory.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), factory.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Fact]
    public async Task Readiness_checks_database_but_never_exposes_internal_failures()
    {
        using var factory = new ProductionSecurityApiFactory();
        using var client = factory.Client();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("postgres", content); Assert.Contains("Unhealthy", content);
        foreach (var forbidden in new[] { "Password", "127.0.0.1", "Exception", "StackTrace", "ConnectionString" })
            Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/Auth/login")]
    [InlineData("/health/live/extra")]
    [InlineData("/health/ready/extra")]
    public async Task Non_exact_probe_paths_keep_https_redirection(string path)
    {
        using var factory = new ProductionSecurityApiFactory(new() { ["https_port"] = "443" });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://example.test"), AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Lifecycle_is_unready_before_start_and_immediately_at_stopping()
    {
        using var lifetime = new TestLifetime();
        using var readiness = new ApplicationReadiness(lifetime);
        await readiness.StartAsync(default);
        Assert.Equal(HealthStatus.Unhealthy, (await readiness.CheckHealthAsync(new())).Status);
        lifetime.Started.Cancel();
        Assert.Equal(HealthStatus.Healthy, (await readiness.CheckHealthAsync(new())).Status);
        lifetime.StopApplication();
        Assert.Equal(HealthStatus.Unhealthy, (await readiness.CheckHealthAsync(new())).Status);
        await readiness.StopAsync(default);
        Assert.Equal(HealthStatus.Unhealthy, (await readiness.CheckHealthAsync(new())).Status);
    }

    [Fact]
    public void Resource_has_only_service_version_and_environment_not_tenant_metadata()
    {
        var resource = OperationsConfiguration.Resource(new(), "Synthetic").Build().Attributes.ToDictionary(x => x.Key, x => x.Value);
        Assert.Equal("hfpos-api", resource["service.name"]);
        Assert.Equal(OperationsConfiguration.ReleaseVersion, resource["service.version"]);
        Assert.Equal("Synthetic", resource["deployment.environment"]);
        Assert.DoesNotContain(resource.Keys, key => key.Contains("Company") || key.Contains("User") || key.Contains("RUC"));
        var disabled = new ObservabilityOptions { ServiceName = "" };
        Assert.True(OperationsConfiguration.IsValid(disabled));
        Assert.Contains(OperationsConfiguration.Resource(disabled, "Synthetic").Build().Attributes,
            attribute => attribute.Key == "service.name" && attribute.Value.Equals("hfpos-api"));
    }

    [Fact]
    public async Task Completion_event_has_correlation_status_elapsed_and_no_secret_payloads()
    {
        var logger = new CaptureLogger();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST"; context.Request.Path = "/api/platform/Auth/login";
        context.Request.QueryString = new("?password=SECRET-QUERY");
        context.Request.Headers.Authorization = "Bearer SECRET-TOKEN";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("SECRET-BODY"));
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        await new RequestLoggingScopeMiddleware(c => { c.Response.StatusCode = 401; return Task.CompletedTask; }, logger).InvokeAsync(context);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(activity.TraceId.ToString(), entry["TraceId"]);
        Assert.Equal(activity.SpanId.ToString(), entry["SpanId"]);
        Assert.Equal(401, entry["StatusCode"]);
        Assert.True((double)entry["ElapsedMilliseconds"]! >= 0);
        var text = string.Join(" ", entry.Select(x => $"{x.Key}={x.Value}"));
        Assert.DoesNotContain("SECRET", text); Assert.DoesNotContain("Authorization", text); Assert.DoesNotContain("password", text);
    }

    [Fact]
    public async Task Probe_completion_is_debug_and_not_a_per_tenant_info_metric()
    {
        var logger = new CaptureLogger(); var context = new DefaultHttpContext(); context.Request.Path = "/health/live";
        await new RequestLoggingScopeMiddleware(_ => Task.CompletedTask, logger).InvokeAsync(context);
        Assert.Equal(LogLevel.Debug, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Unreachable_local_otlp_does_not_break_requests_liveness_or_lifecycle()
    {
        var capture = new CaptureProcessor();
        using var source = new ProductionSecurityApiFactory(new()
        { ["Observability:Enabled"] = "true", ["Observability:OtlpEndpoint"] = "http://127.0.0.1:1", ["Observability:TraceSampleRatio"] = "1" });
        using var factory = source.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureOpenTelemetryTracerProvider((_, traces) => traces.AddProcessor(capture))));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/missing?password=SECRET-QUERY");
        request.Headers.Add("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        var response = await client.SendAsync(request);
        await response.Content.ReadAsStringAsync();
        Assert.True(SpinWait.SpinUntil(() => capture.Activities.Count > 0, 2000));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var activity = Assert.Single(capture.Activities);
        Assert.NotEqual(default, activity.TraceId);
        Assert.DoesNotContain(activity.TagObjects, tag => tag.Key.Contains("url") || tag.Key.Contains("password"));
        Assert.DoesNotContain("SECRET", activity.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Single(capture.Activities); // Health traces filtered.
        Assert.Equal(HealthStatus.Healthy, (await factory.Services.GetRequiredService<ApplicationReadiness>().CheckHealthAsync(new())).Status);
    }

    private sealed class CaptureProcessor : BaseProcessor<Activity>
    {
        public List<Activity> Activities { get; } = [];
        public override void OnEnd(Activity activity) => Activities.Add(activity);
    }

    private sealed class CaptureLogger : ILogger<RequestLoggingScopeMiddleware>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Levels.Add(level); Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(x => x.Key, x => x.Value)); }
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        public CancellationTokenSource Started { get; } = new();
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => Started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopping.Token;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() { Started.Dispose(); _stopping.Dispose(); }
    }
}
