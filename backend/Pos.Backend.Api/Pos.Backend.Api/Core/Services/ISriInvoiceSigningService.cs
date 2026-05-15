using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriInvoiceSigningService
{
    Task<SaleDto> SignInvoiceDraftAsync(int saleId);

    Task<string> GetSignedXmlAsync(int saleId);
}
