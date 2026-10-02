using System.Diagnostics;
using Pos.Backend.Api.Configuration;

namespace Pos.Backend.Api.WebApi.Middleware;

public class RequestLoggingScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingScopeMiddleware> _logger;

    public RequestLoggingScopeMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToString() ?? "";
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = traceId,
            ["SpanId"] = spanId,
            ["RequestPath"] = context.Request.Path.Value,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            try { await _next(context); }
            finally
            {
                var level = OperationsConfiguration.IsProbe(context.Request.Path) && context.Response.StatusCode < 500
                    ? LogLevel.Debug : LogLevel.Information;
                _logger.Log(level, "HTTP request completed {TraceId} {SpanId} {RequestMethod} {RequestPath} {StatusCode} {ElapsedMilliseconds}",
                    traceId, spanId, context.Request.Method, context.Request.Path.Value,
                    context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }
}
