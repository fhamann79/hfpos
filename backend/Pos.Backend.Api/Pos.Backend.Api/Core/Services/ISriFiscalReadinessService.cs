using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface ISriFiscalReadinessService
{
    Task<SriFiscalReadinessDto> GetReadinessAsync();
}
