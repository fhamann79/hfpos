using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.WebApi.Filters;

public class OperationalContextFilter : IAsyncActionFilter
{
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public OperationalContextFilter(IOperationalContextAccessor operationalContextAccessor)
    {
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            await _operationalContextAccessor.GetRequiredContextAsync();
            await next();
        }
        catch (OperationalContextException ex)
        {
            context.Result = new ObjectResult(new ApiErrorResponse
            {
                Error = ex.ErrorCode,
                Details = ex.Details
            })
            {
                StatusCode = ex.StatusCode
            };
        }
    }
}
