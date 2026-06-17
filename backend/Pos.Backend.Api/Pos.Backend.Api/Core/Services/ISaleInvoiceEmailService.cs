using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISaleInvoiceEmailService
{
    Task<SendSaleInvoiceEmailResultDto> SendAuthorizedInvoiceEmailAsync(
        int saleId,
        SendSaleInvoiceEmailRequestDto dto);

    Task<IReadOnlyList<SaleInvoiceEmailDeliveryDto>> GetDeliveriesAsync(int saleId);
}
