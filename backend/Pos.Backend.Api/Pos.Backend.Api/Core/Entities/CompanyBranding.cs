namespace Pos.Backend.Api.Core.Entities;

public class CompanyBranding
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public byte[]? LogoBytes { get; set; }

    public string? LogoContentType { get; set; }

    public string? LogoFileName { get; set; }

    public long? LogoSizeBytes { get; set; }

    public DateTime? LogoUpdatedAt { get; set; }

    public string? PrimaryColor { get; set; }

    public string? DocumentFooterText { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
