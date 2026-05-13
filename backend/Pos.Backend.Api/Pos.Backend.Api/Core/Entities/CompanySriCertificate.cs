namespace Pos.Backend.Api.Core.Entities;

public class CompanySriCertificate
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public byte[] EncryptedCertificateBytes { get; set; } = Array.Empty<byte>();

    public byte[] EncryptedPassword { get; set; } = Array.Empty<byte>();

    public string Thumbprint { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public DateTime NotBefore { get; set; }

    public DateTime NotAfter { get; set; }

    public bool HasPrivateKey { get; set; }

    public bool IsActive { get; set; }

    public DateTime UploadedAt { get; set; }

    public int UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public DateTime? DeactivatedAt { get; set; }

    public int? DeactivatedByUserId { get; set; }
    public User? DeactivatedByUser { get; set; }
}
