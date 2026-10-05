namespace Pos.Backend.Api.WebApi.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (context.Request.Path.Equals("/api/Auth/login", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.Equals("/api/platform/auth/login", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/api/account/recovery", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/api/platform/account/recovery", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        return next(context);
    }
}
