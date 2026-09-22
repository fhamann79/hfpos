using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPurchaseReceiptQueryService
{
    Task<PurchaseReceiptListResultDto> GetListAsync(PurchaseReceiptListQueryDto query);
}
