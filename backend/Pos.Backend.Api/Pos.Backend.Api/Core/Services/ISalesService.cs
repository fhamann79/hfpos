using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Core.Services;

public interface ISalesService
{
    Task<IReadOnlyList<SaleListItemDto>> GetSalesAsync(DateTime? from, DateTime? to, SaleStatus? status, string? search, int? userId);
    Task<SaleDto?> GetByIdAsync(int id);
    Task<SaleDto> CreateAsync(SaleCreateDto dto);
    Task<SaleDto> VoidAsync(int id, VoidSaleDto dto);
}
