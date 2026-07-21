using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriCreditNoteSubmissionService
{
    Task<CreditNoteDto> SubmitSignedAsync(int creditNoteId);

    Task<CreditNoteDto> CheckAuthorizationAsync(int creditNoteId);

    Task<string> GetAuthorizedXmlAsync(int creditNoteId);

    Task<SriRideDto> GetRideAsync(int creditNoteId);

    Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(
        int creditNoteId);
}
