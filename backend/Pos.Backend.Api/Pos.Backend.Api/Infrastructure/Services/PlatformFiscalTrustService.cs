using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PlatformFiscalTrustService(PosDbContext db, IPlatformContextAccessor platform,
    FiscalTrustRegistry registry, FiscalPreflightCache cache) : IPlatformFiscalTrustService
{
    public async Task<object> RegistryAsync()
    {
        await platform.GetRequiredContextAsync();
        return new { registry.Version, Status = registry.StatusAt(DateTime.UtcNow), registry.ValidFrom, registry.ValidUntil,
            Authorities = registry.PublicAuthorities, RevocationSources = registry.PublicRevocationSources };
    }

    public async Task<FiscalPreflightPageDto> PreflightAsync(int page, int pageSize, CancellationToken ct)
    {
        await platform.GetRequiredContextAsync();
        return await new FiscalPreflightReader(db, registry, cache).ReadAsync(page, pageSize, ct);
    }
}

// Metadata/cache-only reader, shared with the isolated operational CLI. Never evaluates or decrypts.
public sealed class FiscalPreflightReader(PosDbContext db, FiscalTrustRegistry registry, FiscalPreflightCache cache)
{
    public async Task<FiscalPreflightPageDto> ReadAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await db.Companies.AsNoTracking().CountAsync(ct);
        var snapshots = await FiscalTrustSnapshots.Query(db).OrderBy(s => s.CompanyId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return new(registry.Version, registry.StatusAt(now), now, cache.LastCompletedCycleAt, total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize), snapshots.Select(s => cache.Read(s, registry, now)).ToArray());
    }
}
