using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CreditNoteListItemDto
{
    public int Id { get; set; }

    public int OriginalSaleId { get; set; }

    public string? Number { get; set; }

    public string? EstablishmentCodeSnapshot { get; set; }

    public string? EmissionPointCodeSnapshot { get; set; }

    public SaleDocumentStatus DocumentStatus { get; set; }

    public DateTime? DocumentIssuedAt { get; set; }

    public DateOnly BusinessDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? CancellationReason { get; set; }

    public int? CancelledByUserId { get; set; }

    public string? CancelledByUsername { get; set; }

    public bool CanCancelDraft { get; set; }
}
