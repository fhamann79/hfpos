namespace Pos.Backend.Api.Core.Entities;

public class CompanyEmailSettings
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public string EncryptionMode { get; set; } = "StartTls";

    public string? SmtpUsername { get; set; }

    public string? SmtpPasswordProtected { get; set; }

    public string? FromEmail { get; set; }

    public string? FromDisplayName { get; set; }

    public string? ReplyToEmail { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime? LastTestedAt { get; set; }

    public bool? LastTestSucceeded { get; set; }

    public string? LastTestMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
