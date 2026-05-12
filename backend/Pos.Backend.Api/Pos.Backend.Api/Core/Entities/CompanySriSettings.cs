namespace Pos.Backend.Api.Core.Entities;

public class CompanySriSettings
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int Environment { get; set; }

    public int EmissionType { get; set; }

    public bool IsEnabled { get; set; }

    public bool CertificateConfigured { get; set; }

    public DateTime? CertificateExpiresAt { get; set; }

    public int? LastUpdatedByUserId { get; set; }
    public User? LastUpdatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
