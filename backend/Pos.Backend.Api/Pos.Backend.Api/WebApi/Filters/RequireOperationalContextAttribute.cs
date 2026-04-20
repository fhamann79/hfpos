using Microsoft.AspNetCore.Mvc;

namespace Pos.Backend.Api.WebApi.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireOperationalContextAttribute : TypeFilterAttribute
{
    public RequireOperationalContextAttribute() : base(typeof(OperationalContextFilter))
    {
    }
}
