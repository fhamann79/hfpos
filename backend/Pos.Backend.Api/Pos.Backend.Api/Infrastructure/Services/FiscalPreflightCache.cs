using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalPreflightCache(IOptions<FiscalTrustOptions> options)
{
    private sealed record Entry(FiscalTenantTrustSnapshot Snapshot, DateTime AsOf, FiscalTrustDecision? Decision, string Reason);
    private readonly ConcurrentDictionary<int, Entry> _entries = new();
    public DateTime? LastCompletedCycleAt { get; private set; }
    internal void Completed() => LastCompletedCycleAt = DateTime.UtcNow;

    internal void Record(FiscalTenantTrustSnapshot snapshot, FiscalTrustDecision? decision, string reason)
    {
        if (_entries.Count >= Math.Clamp(options.Value.MaximumCachedTenants, 1, 10_000)
            && !_entries.ContainsKey(snapshot.CompanyId)) return;
        _entries[snapshot.CompanyId] = new(snapshot, DateTime.UtcNow, decision, reason);
    }

    internal FiscalTenantPreflightDto Read(FiscalTenantTrustSnapshot snapshot, FiscalTrustRegistry registry, DateTime now)
    {
        if (!_entries.TryGetValue(snapshot.CompanyId, out var entry))
            return new(snapshot, "UNKNOWN", "FISCAL_PREFLIGHT_NOT_OBSERVED", null, null);
        if (entry.Snapshot != snapshot)
            return new(snapshot, "UNKNOWN", "FISCAL_PREFLIGHT_SNAPSHOT_CHANGED", entry.AsOf, null);
        if (entry.AsOf <= now.AddMinutes(-5) || entry.Decision?.ValidUntil <= now
            || entry.Decision is { } d && d.TrustVersion != registry.Version)
            return new(snapshot, "UNKNOWN", "FISCAL_PREFLIGHT_STALE", entry.AsOf, null);
        return new(snapshot, entry.Decision is null ? "UNKNOWN" : entry.Decision.Eligible ? "CANDIDATE_ELIGIBLE" : "NO_GO",
            entry.Reason, entry.AsOf, entry.Decision);
    }
}
