using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class SriSubmissionAttemptDto
{
    public int Id { get; set; }

    public int SaleId { get; set; }

    public string AccessKey { get; set; } = string.Empty;

    public int Environment { get; set; }

    public SriSubmissionAttemptType AttemptType { get; set; }

    public SriSubmissionAttemptStatus Status { get; set; }

    public string? ReceptionStatus { get; set; }

    public string? AuthorizationStatus { get; set; }

    public string? AuthorizationNumber { get; set; }

    public DateTime? AuthorizationDate { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? SriMessageIdentifier { get; set; }

    public string? SriMessageType { get; set; }

    public string? SriMessage { get; set; }

    public string? SriAdditionalInfo { get; set; }

    public DateTime CreatedAt { get; set; }

    public int CreatedByUserId { get; set; }
}
