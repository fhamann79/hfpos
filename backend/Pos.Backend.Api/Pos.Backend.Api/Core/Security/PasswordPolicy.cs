namespace Pos.Backend.Api.Core.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 256;

    public static bool IsValid(string? password)
        => !string.IsNullOrWhiteSpace(password)
            && password.Length is >= MinimumLength and <= MaximumLength;
}
