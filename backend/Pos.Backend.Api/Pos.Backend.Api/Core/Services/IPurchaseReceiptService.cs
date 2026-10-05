using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPurchaseReceiptService
{
    Task<PurchaseReceiptDto> CreateAsync(PurchaseReceiptCreateDto dto);
}
