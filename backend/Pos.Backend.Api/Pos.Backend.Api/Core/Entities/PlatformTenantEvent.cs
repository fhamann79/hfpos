namespace Pos.Backend.Api.Core.Entities;

public enum PlatformTenantEventType { Provisioned, Suspended, Reactivated }

public sealed class PlatformTenantEvent
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int PlatformUserId { get; set; }
    public PlatformUser PlatformUser { get; set; } = null!;
    public PlatformTenantEventType EventType { get; set; }
    public string? Reason { get; set; }
    public Guid? RequestId { get; set; }
    public DateTime CreatedAt { get; set; }
    // Immutable, password-free normalized inputs used only to compare provisioning retries.
    public string? ProvisioningSnapshot { get; set; }
    public int? InitialAdminUserId { get; set; }
    public User? InitialAdminUser { get; set; }
}
