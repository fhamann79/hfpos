using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Entities;

public class CreditNote
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int EstablishmentId { get; set; }
    public Establishment Establishment { get; set; } = null!;

    public int EmissionPointId { get; set; }
    public EmissionPoint EmissionPoint { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int OriginalSaleId { get; set; }
    public Sale OriginalSale { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string? BuyerNameSnapshot { get; set; }

    public string? BuyerIdentificationTypeSnapshot { get; set; }

    public string? BuyerIdentificationSnapshot { get; set; }

    public string? BuyerAddressSnapshot { get; set; }

    public string? BuyerEmailSnapshot { get; set; }

    public string? OriginalSaleNumberSnapshot { get; set; }

    public string? OriginalSaleAccessKeySnapshot { get; set; }

    public string? OriginalSaleAuthorizationNumberSnapshot { get; set; }

    public DateTime? OriginalSaleAuthorizedAtSnapshot { get; set; }

    public DateTime? OriginalSaleDocumentIssuedAtSnapshot { get; set; }

    public SaleDocumentStatus DocumentStatus { get; set; }

    public string? Number { get; set; }

    public string? EstablishmentCodeSnapshot { get; set; }

    public string? EmissionPointCodeSnapshot { get; set; }

    public int? Sequential { get; set; }

    public DateTime? DocumentIssuedAt { get; set; }

    public string? AccessKey { get; set; }

    public string? AuthorizationNumber { get; set; }

    public DateTime? AuthorizedAt { get; set; }

    public int? SriEnvironment { get; set; }

    public int? SriEmissionType { get; set; }

    public string? SriNumericCode { get; set; }

    public DateTime? SriSubmittedAt { get; set; }

    public string? SriReceptionStatus { get; set; }

    public string? SriAuthorizationStatus { get; set; }

    public string? SriLastSubmissionError { get; set; }

    public DateTime? SriLastCheckedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public decimal GrossSubtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Vat15Subtotal { get; set; }

    public decimal Vat5Subtotal { get; set; }

    public decimal Vat0Subtotal { get; set; }

    public decimal VatExemptSubtotal { get; set; }

    public decimal VatNotSubjectSubtotal { get; set; }

    public decimal Total { get; set; }

    public DateOnly BusinessDate { get; set; }

    public string TimeZoneIdSnapshot { get; set; } = "America/Guayaquil";

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? VoidedAt { get; set; }

    public ICollection<CreditNoteItem> Items { get; set; } = new List<CreditNoteItem>();
}
