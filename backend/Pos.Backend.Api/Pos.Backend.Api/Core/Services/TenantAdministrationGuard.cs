using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Core.Services;

public sealed class TenantAdministrationGuard(PosDbContext context)
{
    public async Task<IDbContextTransaction> BeginChangeAsync(int companyId)
    {
        var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            // Serialize tenant admin writes so concurrent demotions cannot remove the last admin.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {companyId} FOR UPDATE");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public Task<bool> HasActiveAdministratorAsync(int companyId) => context.Users.AnyAsync(u =>
        u.CompanyId == companyId && u.IsActive && u.Role.CompanyId == companyId
        && u.Role.IsActive && u.Role.Code == AppRoles.Admin
        && u.Establishment != null && u.Establishment.IsActive
        && u.Establishment.CompanyId == companyId
        && u.EmissionPoint.IsActive && u.EmissionPoint.EstablishmentId == u.EstablishmentId);
}
