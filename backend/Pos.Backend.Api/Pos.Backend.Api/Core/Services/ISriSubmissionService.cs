using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriSubmissionService
{
    Task<SaleDto> SubmitSignedInvoiceAsync(int saleId);

    Task<SaleDto> CheckAuthorizationAsync(int saleId);

    Task<SriReceptionRecoveryStatus> GetReceptionRecoveryStatusAsync(int saleId, CancellationToken ct);

    Task<SriReceptionRecoveryStatus> RecoverReceptionAsync(int saleId, SriReceptionRecoveryRequest request, CancellationToken ct);

    Task<string> GetAuthorizedXmlAsync(int saleId);

    Task<SriRideDto> GetRideAsync(int saleId);

    Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(int saleId);
}
