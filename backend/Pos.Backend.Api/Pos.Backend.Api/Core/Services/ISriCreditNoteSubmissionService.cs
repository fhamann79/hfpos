using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriCreditNoteSubmissionService
{
    Task<CreditNoteDto> SubmitSignedAsync(int creditNoteId);

    Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(
        int creditNoteId);
}
