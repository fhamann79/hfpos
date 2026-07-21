using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface ISriRidePdfService
{
    Task<SriRidePdfFileResult> GenerateAsync(int saleId);

    Task<SriRidePdfFileResult> GenerateCreditNoteAsync(int creditNoteId);
}
