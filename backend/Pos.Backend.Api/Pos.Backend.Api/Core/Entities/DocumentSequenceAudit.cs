using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class DocumentSequenceAudit
{
    public int Id { get; set; }

    public int DocumentSequenceId { get; set; }
    public DocumentSequence DocumentSequence { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;

    public FiscalDocumentType DocumentType { get; set; }

    public int? PreviousCurrentNumber { get; set; }

    public int NewCurrentNumber { get; set; }

    public int? PreviousNextNumber { get; set; }

    public int NewNextNumber { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
