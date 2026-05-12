using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.DTOs;

public class CompanyFiscalSettingsDto
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string? MatrixAddress { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsAccountingRequired { get; set; }
    public string? SpecialTaxpayerNumber { get; set; }
    public string? TaxpayerRegime { get; set; }
    public bool IsActive { get; set; }
}

public class UpdateCompanyFiscalSettingsDto
{
    public string Name { get; set; } = string.Empty;
    public string? TradeName { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string? MatrixAddress { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsAccountingRequired { get; set; }
    public string? SpecialTaxpayerNumber { get; set; }
    public string? TaxpayerRegime { get; set; }
}

public class CompanySriSettingsDto
{
    public int CompanyId { get; set; }
    public int Environment { get; set; }
    public int EmissionType { get; set; }
    public bool IsEnabled { get; set; }
    public bool CertificateConfigured { get; set; }
    public DateTime? CertificateExpiresAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpdateCompanySriSettingsDto
{
    public int Environment { get; set; }
    public int EmissionType { get; set; }
    public bool IsEnabled { get; set; }
}

public class DocumentSequenceDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public string EstablishmentCode { get; set; } = string.Empty;
    public string EstablishmentName { get; set; } = string.Empty;
    public int EmissionPointId { get; set; }
    public string EmissionPointCode { get; set; } = string.Empty;
    public string EmissionPointName { get; set; } = string.Empty;
    public SaleDocumentType DocumentType { get; set; }
    public int CurrentNumber { get; set; }
    public int NextNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MaxUsedSequential { get; set; }
}

public class CreateDocumentSequenceDto
{
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public SaleDocumentType DocumentType { get; set; }
    public int NextNumber { get; set; }
    public string? Reason { get; set; }
}

public class UpdateDocumentSequenceDto
{
    public int NextNumber { get; set; }
    public string? Reason { get; set; }
}

public class DocumentSequenceAuditDto
{
    public int Id { get; set; }
    public int DocumentSequenceId { get; set; }
    public SaleDocumentType DocumentType { get; set; }
    public int? PreviousCurrentNumber { get; set; }
    public int NewCurrentNumber { get; set; }
    public int? PreviousNextNumber { get; set; }
    public int NewNextNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
