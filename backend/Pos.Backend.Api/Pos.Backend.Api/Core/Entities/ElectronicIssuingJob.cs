using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public sealed class ElectronicIssuingJob
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public ElectronicDocumentKind DocumentType { get; set; } = ElectronicDocumentKind.Invoice;
    public int UserId { get; set; }
    public long? FiscalDelegationId { get; set; }
    public string AccessKey { get; set; } = string.Empty;
    public int Environment { get; set; }
    public string DraftHash { get; set; } = string.Empty;
    public bool AutomaticRequested { get; set; }
    public ElectronicIssuingJobState State { get; set; }
    public ElectronicIssuingPhase Phase { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public long Fence { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    // A committed intent forbids another reception, even if the process dies before calling SRI.
    public DateTime? ReceptionStartedAt { get; set; }
    public string? SafeError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
