namespace Pos.Backend.Api.Core.Entities;

public class SaleInvoiceEmailDelivery
{
    public int Id { get; set; }

    public int? SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? CreditNoteId { get; set; }
    public CreditNote? CreditNote { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;

    public string ToEmail { get; set; } = string.Empty;

    public string? CcEmail { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public string? DocumentNumberSnapshot { get; set; }

    public string? AuthorizationNumberSnapshot { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
