using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IInitialDataService
{
    Task<string> GetTemplateAsync(string kind);
    Task<InitialDataPreviewDto> PreviewAsync(InitialDataPreviewRequest request);
    Task<InitialDataResultDto> ConfirmAsync(InitialDataConfirmRequest request);
    Task<IReadOnlyList<InitialDataResultDto>> GetBatchesAsync(int page = 1);
    Task<TenantReadinessDto> GetReadinessAsync();
}
