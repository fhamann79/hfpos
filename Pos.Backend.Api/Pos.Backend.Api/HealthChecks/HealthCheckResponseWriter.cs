using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pos.Backend.Api.HealthChecks;

public static class HealthCheckResponseWriter
{
    public static async Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString()
                })
                .ToArray()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
