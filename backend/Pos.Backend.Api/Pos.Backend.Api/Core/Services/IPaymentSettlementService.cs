using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IPaymentSettlementService
{
    Task<PaymentReconciliationDto> GetReconciliationAsync(DateOnly? businessDate);
    Task<PaymentSettlementDto> CreateAsync(PaymentSettlementCreateDto request);
    Task<PagedResultDto<PaymentSettlementDto>> GetAsync(PaymentSettlementQueryDto query);
    Task<PaymentSettlementDto?> GetByIdAsync(int id);
}
