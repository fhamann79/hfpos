namespace Pos.Backend.Api.Core.Models;

public class SriAccessKeyRequest
{
    public DateTime EmissionDate { get; set; }

    public string DocumentCode { get; set; } = string.Empty;

    public string IssuerRuc { get; set; } = string.Empty;

    public int Environment { get; set; }

    public string EstablishmentCode { get; set; } = string.Empty;

    public string EmissionPointCode { get; set; } = string.Empty;

    public int Sequential { get; set; }

    public int EmissionType { get; set; }

    public string NumericCodeSeed { get; set; } = string.Empty;
}
