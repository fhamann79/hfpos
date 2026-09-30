namespace Pos.Backend.Api.Core.Entities;

public sealed class PlatformUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public long SessionVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
}
