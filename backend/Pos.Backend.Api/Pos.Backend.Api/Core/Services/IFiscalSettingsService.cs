using Microsoft.AspNetCore.Http;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public interface IFiscalSettingsService
{
    Task<CompanyFiscalSettingsDto> GetCompanyFiscalSettingsAsync();

    Task<CompanyFiscalSettingsDto> UpdateCompanyFiscalSettingsAsync(UpdateCompanyFiscalSettingsDto dto);

    Task<CompanyBrandingDto> GetCompanyBrandingAsync();

    Task<CompanyBrandingDto> UpdateCompanyBrandingAsync(UpdateCompanyBrandingDto dto);

    Task<CompanyBrandingDto> UploadCompanyLogoAsync(IFormFile? file);

    Task<CompanyLogoFileResult> GetCompanyLogoAsync();

    Task DeleteCompanyLogoAsync();

    Task<CompanySriSettingsDto> GetCompanySriSettingsAsync();

    Task<CompanySriSettingsDto> UpdateCompanySriSettingsAsync(UpdateCompanySriSettingsDto dto);

    Task<IReadOnlyList<DocumentSequenceDto>> GetDocumentSequencesAsync(
        int? establishmentId,
        int? emissionPointId,
        SaleDocumentType? documentType);

    Task<DocumentSequenceDto> CreateDocumentSequenceAsync(CreateDocumentSequenceDto dto);

    Task<DocumentSequenceDto> UpdateDocumentSequenceAsync(int id, UpdateDocumentSequenceDto dto);

    Task<IReadOnlyList<DocumentSequenceAuditDto>> GetDocumentSequenceAuditsAsync(int id);
}
