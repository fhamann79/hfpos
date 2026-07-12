using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Models;

public class FiscalDocumentNumberAssignment
{
    public FiscalDocumentType DocumentType { get; set; }

    public string Number { get; set; } = string.Empty;

    public string EstablishmentCode { get; set; } = string.Empty;

    public string EmissionPointCode { get; set; } = string.Empty;

    public int Sequential { get; set; }

    public DateTime IssuedAt { get; set; }
}
