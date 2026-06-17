using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync();
}
