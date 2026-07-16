using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICreditNoteService
{
    Task<CreditNoteEligibilityDto> GetEligibilityAsync(int originalSaleId);

    Task<CreditNoteDto> CreateDraftAsync(CreateCreditNoteDraftDto dto);

    Task<IReadOnlyList<CreditNoteListItemDto>> GetByOriginalSaleAsync(int originalSaleId);

    Task<CreditNoteDto> GetByIdAsync(int creditNoteId);

    Task<CreditNoteDto> PrepareSriDraftAsync(int creditNoteId);

    Task<string> GetSriXmlDraftAsync(int creditNoteId);

    Task<CreditNoteDto> CancelDraftAsync(
        int creditNoteId,
        CancelCreditNoteDraftDto dto);
}
