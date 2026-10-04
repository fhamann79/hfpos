using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class OperationalIdentityProtection
{
    public static bool ValidCode(string? code) => code is { Length: 3 } && code != "000"
        && code.All(c => c is >= '0' and <= '9');
    public static bool ValidAddress(string? address) => !string.IsNullOrWhiteSpace(address)
        && address.Trim() != "N/A" && address.Trim().Length <= 250;

    public static async Task<bool> IsUsedAsync(PosDbContext db, int companyId, int? establishmentId = null, int? pointId = null)
    {
        return await db.Sales.AnyAsync(s => s.CompanyId == companyId
                && (!establishmentId.HasValue || s.EstablishmentId == establishmentId)
                && (!pointId.HasValue || s.EmissionPointId == pointId))
            || await db.CreditNotes.AnyAsync(n => n.CompanyId == companyId
                && (!establishmentId.HasValue || n.EstablishmentId == establishmentId)
                && (!pointId.HasValue || n.EmissionPointId == pointId))
            || await db.DocumentSequences.AnyAsync(s => s.CompanyId == companyId
                && (!establishmentId.HasValue || s.EstablishmentId == establishmentId)
                && (!pointId.HasValue || s.EmissionPointId == pointId)
                && (s.CurrentNumber > 0 || db.DocumentSequenceAudits.Any(a => a.DocumentSequenceId == s.Id)));
    }
}
