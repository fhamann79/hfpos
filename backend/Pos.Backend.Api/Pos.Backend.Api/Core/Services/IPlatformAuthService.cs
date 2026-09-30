using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPlatformAuthService
{
    Task<string> LoginAsync(LoginDto request);
}
