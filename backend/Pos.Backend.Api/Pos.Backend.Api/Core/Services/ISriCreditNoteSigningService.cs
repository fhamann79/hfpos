using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriCreditNoteSigningService
{
    Task<CreditNoteDto> SignDraftAsync(int creditNoteId);

    Task<string> GetSignedXmlAsync(int creditNoteId);
}
