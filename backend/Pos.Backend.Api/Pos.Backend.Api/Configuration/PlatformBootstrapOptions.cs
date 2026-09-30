namespace Pos.Backend.Api.Configuration;

public sealed class PlatformBootstrapOptions
{
    public bool Enabled { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
