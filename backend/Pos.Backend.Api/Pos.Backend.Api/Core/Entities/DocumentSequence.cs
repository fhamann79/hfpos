using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class DocumentSequence
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; }

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; }

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; }

    public SaleDocumentType DocumentType { get; set; }

    public int CurrentNumber { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
