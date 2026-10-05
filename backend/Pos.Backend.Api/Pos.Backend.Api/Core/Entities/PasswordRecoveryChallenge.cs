namespace Pos.Backend.Api.Core.Entities;

public sealed class PasswordRecoveryChallenge
{
    public Guid Id { get; set; }
    public bool IsPlatform { get; set; }
    public int? CompanyId { get; set; }
    public int? UserId { get; set; }
    public int? PlatformUserId { get; set; }
    public int CreatedById { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public byte[] PasswordFingerprint { get; set; } = [];
    public long SessionVersion { get; set; }
    public int? RoleId { get; set; }
    public long? RoleVersion { get; set; }
    public int? EstablishmentId { get; set; }
    public int? EmissionPointId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string Reason { get; set; } = "";
    public string DeliveryReference { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
}

public sealed class PasswordSecurityAudit
{
    public long Id { get; set; }
    public bool IsPlatform { get; set; }
    public int? CompanyId { get; set; }
    public int? ActorId { get; set; }
    public int? TargetId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string Event { get; set; } = "";
    public string Reason { get; set; } = "";
    public string DeliveryReference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
