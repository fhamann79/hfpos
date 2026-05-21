using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class SriSubmissionAttempt
{
    public int Id { get; set; }

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;

    public string AccessKey { get; set; } = string.Empty;

    public int Environment { get; set; }

    public SriSubmissionAttemptType AttemptType { get; set; }

    public SriSubmissionAttemptStatus Status { get; set; }

    public string? ReceptionStatus { get; set; }

    public string? AuthorizationStatus { get; set; }

    public string? AuthorizationNumber { get; set; }

    public DateTime? AuthorizationDate { get; set; }

    public string? RequestXmlSnapshot { get; set; }

    public string? ResponseXml { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? SriMessageIdentifier { get; set; }

    public string? SriMessageType { get; set; }

    public string? SriMessage { get; set; }

    public string? SriAdditionalInfo { get; set; }

    public DateTime CreatedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
}
