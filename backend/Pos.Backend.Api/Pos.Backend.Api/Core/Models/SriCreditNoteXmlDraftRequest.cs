using Pos.Backend.Api.Core.Entities;

namespace Pos.Backend.Api.Core.Models;

public class SriCreditNoteXmlDraftRequest
{
    public CreditNote CreditNote { get; set; } = null!;

    public Company Company { get; set; } = null!;

    public Establishment Establishment { get; set; } = null!;

    public int Environment { get; set; }

    public int EmissionType { get; set; }

    public DateOnly FiscalEmissionDate { get; set; }

    public DateOnly OriginalInvoiceFiscalEmissionDate { get; set; }
}
