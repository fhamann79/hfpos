using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface ISriAccessKeyService
{
    SriAccessKeyResult GenerateInvoiceAccessKey(SriAccessKeyRequest request);

    SriAccessKeyResult GenerateCreditNoteAccessKey(SriAccessKeyRequest request);

    int CalculateModulo11CheckDigit(string accessKeyBase48);
}
