namespace Pos.Backend.Api.Core.Models;

public sealed record PlatformContext(int UserId, string Username, string Email, long SessionVersion);

public sealed class PlatformException(string errorCode, int statusCode = 400) : Exception(errorCode)
{
    public int StatusCode { get; } = statusCode;
}
