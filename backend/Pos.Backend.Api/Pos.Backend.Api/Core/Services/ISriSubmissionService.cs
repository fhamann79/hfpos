using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriSubmissionService
{
    Task<SaleDto> SubmitSignedInvoiceAsync(int saleId);

    Task<SaleDto> CheckAuthorizationAsync(int saleId);

    Task<string> GetAuthorizedXmlAsync(int saleId);

    Task<SriRideDto> GetRideAsync(int saleId);

    Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(int saleId);
}
