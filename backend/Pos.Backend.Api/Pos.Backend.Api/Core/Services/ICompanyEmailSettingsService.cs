using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface ICompanyEmailSettingsService
{
    Task<CompanyEmailSettingsDto> GetAsync();

    Task<CompanyEmailSettingsDto> UpdateAsync(UpdateCompanyEmailSettingsDto dto);

    Task<CompanyEmailTestResultDto> SendTestAsync(TestCompanyEmailSettingsDto dto);

    Task<CompanyEmailSenderSettings> GetConfiguredSenderSettingsAsync();
}
