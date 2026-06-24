using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public interface ICashSessionService
{
    Task<CashSessionDto?> GetCurrentAsync();

    Task<IReadOnlyList<CashSessionListItemDto>> GetListAsync(
        DateTime? from,
        DateTime? to,
        CashSessionStatus? status,
        int? userId);

    Task<CashSessionDto?> GetByIdAsync(int id);

    Task<CashSessionDto> OpenAsync(OpenCashSessionDto dto);

    Task<CashSessionDto> AddMovementAsync(int id, CreateCashMovementDto dto);

    Task<CashSessionDto> CloseAsync(int id, CloseCashSessionDto dto);

    Task<CashSession> GetRequiredOpenSessionForCurrentContextAsync();
}
