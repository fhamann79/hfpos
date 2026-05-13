using Microsoft.AspNetCore.Http;
using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriCertificateService
{
    Task<CompanySriCertificateDto> GetCurrentCertificateAsync();

    Task<CompanySriCertificateDto> UploadCertificateAsync(IFormFile? file, string? password);

    Task DeactivateCertificateAsync();
}
