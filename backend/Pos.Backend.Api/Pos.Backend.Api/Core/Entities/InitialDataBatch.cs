namespace Pos.Backend.Api.Core.Entities;

public sealed class InitialDataBatch
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public int RowCount { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
