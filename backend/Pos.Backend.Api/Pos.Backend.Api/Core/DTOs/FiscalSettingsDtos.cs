using Microsoft.AspNetCore.Http;
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

public class CompanySriCertificateDto
{
    public int CompanyId { get; set; }
    public bool CertificateConfigured { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public bool HasPrivateKey { get; set; }
    public DateTime UploadedAt { get; set; }
    public int UploadedByUserId { get; set; }
    public bool IsActive { get; set; }
    public int DaysUntilExpiration { get; set; }
    public bool IsExpired { get; set; }
}

public class SriFiscalReadinessDto
{
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public int? Environment { get; set; }
    public string EnvironmentLabel { get; set; } = string.Empty;
    public bool IsReadyForSandboxSubmission { get; set; }
    public bool IsReadyForProductionSubmission { get; set; }
    public bool HasBlockingErrors { get; set; }
    public bool HasWarnings { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int BlockingErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int SuccessCount { get; set; }
    public IReadOnlyList<SriFiscalReadinessCheckDto> Checks { get; set; } = Array.Empty<SriFiscalReadinessCheckDto>();
}

public class SriFiscalReadinessCheckDto
{
    public string Category { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public bool IsBlocking { get; set; }
}

public class UploadSriCertificateRequest
{
    public IFormFile? File { get; set; }
    public string? Password { get; set; }
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
