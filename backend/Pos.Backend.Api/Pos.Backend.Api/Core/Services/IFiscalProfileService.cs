using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.Services;

public interface IFiscalProfileService
{
    Task<IReadOnlyList<FiscalProfileDto>> GetHistoryAsync();
    Task<FiscalProfileDto> DeclareAsync(DeclareFiscalProfileDto dto);
    Task<ProductFiscalStateDto> GetProductClassificationAsync(int productId);
    Task<ProductFiscalClassificationDto> DeclareProductClassificationAsync(int productId, DeclareProductFiscalClassificationDto dto);
    Task<CompanyFiscalProfile> ResolveAsync(int companyId, DateOnly fiscalDate);
    Task<FiscalIssuerSnapshot> SnapshotAsync(OperationalContext context, DateOnly fiscalDate);
    Task<IReadOnlyDictionary<int, ProductFiscalClassification>> ResolveClassificationsAsync(int companyId, IReadOnlyCollection<int> productIds);
}
