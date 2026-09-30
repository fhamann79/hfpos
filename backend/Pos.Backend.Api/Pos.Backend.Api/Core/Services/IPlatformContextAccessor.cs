using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IPlatformContextAccessor
{
    Task<PlatformContext> GetRequiredContextAsync();
}
