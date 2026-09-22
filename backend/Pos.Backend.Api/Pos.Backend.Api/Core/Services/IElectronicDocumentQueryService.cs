using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public interface IElectronicDocumentQueryService
{
    Task<ElectronicDocumentListResultDto> GetListAsync(ElectronicDocumentQueryDto query);

    Task<ElectronicDocumentDetailDto?> GetByIdAsync(ElectronicDocumentKind kind, int id);
}
