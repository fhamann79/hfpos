namespace Pos.Backend.Api.Core.Entities;

public sealed class FiscalDelegation
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public long Revision { get; set; }
    public int EnabledByUserId { get; set; }
    public int AuthorizerEstablishmentId { get; set; }
    public int AuthorizerEmissionPointId { get; set; }
    public DateTime EnabledAt { get; set; }
    public int? DisabledByUserId { get; set; }
    public int? DisabledByPlatformUserId { get; set; }
    public DateTime? DisabledAt { get; set; }
}

public sealed class FiscalDelegationAudit
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public long Revision { get; set; }
    public bool PreviousEnabled { get; set; }
    public bool Enabled { get; set; }
    public int? UserId { get; set; }
    public int? PlatformUserId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}
