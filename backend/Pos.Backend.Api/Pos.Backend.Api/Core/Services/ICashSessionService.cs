using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public interface ICashSessionService
{
    Task<CashSessionDto?> GetCurrentAsync();

    Task<CashSessionListResultDto> GetListAsync(CashSessionListQueryDto query);

    Task<CashSessionDto?> GetByIdAsync(int id);

    Task<CashSessionDto> OpenAsync(OpenCashSessionDto dto);

    Task<CashSessionDto> AddMovementAsync(int id, CreateCashMovementDto dto);

    Task<CashSessionDto> CloseAsync(int id, CloseCashSessionDto dto);

    Task<CashSession> GetRequiredOpenSessionForCurrentContextAsync();

    Task<CashMovement> RegisterCreditNoteRefundCashOutAsync(
        int creditNoteId, decimal amount, string reason);

    Task<CashMovement> RegisterSaleVoidCashOutAsync(
        int saleId, decimal amount, string reason);
}
