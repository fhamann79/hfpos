using Microsoft.AspNetCore.Authorization;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Middleware;

public sealed class OperationalSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOperationalContextAccessor operationalContext,
        IPlatformContextAccessor platformContext)
    {
        var endpoint = context.GetEndpoint();
        if (context.User.Identity?.IsAuthenticated == true
            && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            // Validate stale sessions before permission policies; the accessor caches per request.
            if (endpoint.Metadata.GetMetadata<RequirePlatformContextAttribute>() is not null)
                await platformContext.GetRequiredContextAsync();
            else
                await operationalContext.GetRequiredContextAsync();
        }

        await next(context);
    }
}
