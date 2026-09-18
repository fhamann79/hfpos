namespace Pos.Backend.Api.Core.Services;

public interface IMasterDataLifecycleService
{
    Task SetCategoryActiveAsync(int companyId, int id, bool active);
    Task SetProductActiveAsync(int companyId, int id, bool active);
    Task SetEstablishmentActiveAsync(int companyId, int id, bool active);
    Task SetEmissionPointActiveAsync(int companyId, int id, bool active);
}
