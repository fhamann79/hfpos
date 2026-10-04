using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Models;
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

    public async Task LockOperationalWriteAsync(OperationalContext operationalContext)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Operational writes require a transaction.");

        // Compatible among operational writers, exclusive with lifecycle/admin changes.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {operationalContext.CompanyId} FOR SHARE");

        await ValidateOperationalContextAsync(operationalContext);
    }

    public Task LockPaymentSettlementFinalizationAsync(OperationalContext operationalContext)
        => LockExclusiveOperationalWriteAsync(operationalContext);

    public async Task LockExclusiveOperationalWriteAsync(OperationalContext operationalContext)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment settlement finalization requires a transaction.");

        // Drain prior operational writers and exclude new ones until the snapshot commits/rolls back.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {operationalContext.CompanyId} FOR UPDATE");

        await ValidateOperationalContextAsync(operationalContext);
    }

    private async Task ValidateOperationalContextAsync(OperationalContext operationalContext)
    {
        if ((operationalContext.UserSessionVersion.HasValue || operationalContext.RoleAuthorizationVersion.HasValue)
            && !await context.Users.AnyAsync(u => u.Id == operationalContext.UserId
                && u.CompanyId == operationalContext.CompanyId
                && u.SessionVersion == operationalContext.UserSessionVersion
                && u.Role.AuthorizationVersion == operationalContext.RoleAuthorizationVersion))
            throw new OperationalContextException("SESSION_STALE", 401);
        var valid = await context.Users.AnyAsync(u =>
            u.Id == operationalContext.UserId && u.Username == operationalContext.Username && u.IsActive
            && u.CompanyId == operationalContext.CompanyId && u.Company.IsActive
            && u.Role.CompanyId == operationalContext.CompanyId && u.Role.IsActive
            && u.EstablishmentId == operationalContext.EstablishmentId
            && u.Establishment != null && u.Establishment.IsActive
            && u.Establishment.CompanyId == operationalContext.CompanyId
            && u.EmissionPointId == operationalContext.EmissionPointId && u.EmissionPoint.IsActive
            && u.EmissionPoint.EstablishmentId == operationalContext.EstablishmentId);
        if (!valid)
            throw new OperationalContextException("CONTEXT_MISMATCH", 401);
    }
}
