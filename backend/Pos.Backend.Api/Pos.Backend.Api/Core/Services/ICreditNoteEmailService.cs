using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ICreditNoteEmailService
{
    Task<SendCreditNoteEmailResultDto> SendAuthorizedCreditNoteEmailAsync(
        int creditNoteId,
        SendCreditNoteEmailRequestDto dto);

    Task<IReadOnlyList<SaleInvoiceEmailDeliveryDto>> GetDeliveriesAsync(
        int creditNoteId);
}
