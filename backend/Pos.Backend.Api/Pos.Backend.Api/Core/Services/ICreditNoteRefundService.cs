using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICreditNoteRefundService
{
    Task<CreditNoteDto> RefundAsync(int creditNoteId, RefundCreditNoteDto dto);
}
