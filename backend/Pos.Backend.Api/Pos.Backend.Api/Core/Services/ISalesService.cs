using Pos.Backend.Api.Core.DTOs;
namespace Pos.Backend.Api.Core.Services;

public interface ISalesService
{
    Task<SaleListResultDto> GetSalesAsync(SaleListQueryDto query);

    Task<SaleCsvExportDto> ExportSalesAsync(SaleListQueryDto query);

    Task<SaleDto?> GetByIdAsync(int id);
    Task<string?> GetSriXmlDraftAsync(int id);
    Task<SaleDto> CreateAsync(SaleCreateDto dto);
    Task<SaleDto> VoidAsync(int id, VoidSaleDto dto);
}
