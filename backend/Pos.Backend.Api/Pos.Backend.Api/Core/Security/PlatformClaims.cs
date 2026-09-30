namespace Pos.Backend.Api.Core.Security;

public static class PlatformClaims
{
    public const string TokenType = "token_type";
    public const string TokenTypeValue = "platform";
    public const string SessionVersion = "platform_session_version";
    public const string AdminRole = "PLATFORM_ADMIN";
}
