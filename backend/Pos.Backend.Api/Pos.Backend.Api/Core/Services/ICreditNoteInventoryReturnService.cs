using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICreditNoteInventoryReturnService
{
    Task<CreditNoteDto> ReturnToInventoryAsync(
        int creditNoteId,
        ReturnCreditNoteInventoryDto dto);
}
