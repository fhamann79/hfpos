using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalPublicChainCache
{
    internal const int MaximumCertificateBytes = 32 * 1024;
    internal const int MaximumEntryBytes = 1024 * 1024;
    internal const int MaximumTotalBytes = 64 * 1024 * 1024;
    private sealed record Entry(DateTimeOffset AsOf, byte[][] Auxiliary, int Size);
    private readonly object _sync = new();
    private readonly Dictionary<(int Company, string Thumbprint), Entry> _entries = new();
    private readonly TimeProvider _clock;
    private readonly int _maximumBytes;
    private int _retainedBytes;

    public FiscalPublicChainCache() : this(TimeProvider.System, MaximumTotalBytes) { }

    internal FiscalPublicChainCache(TimeProvider clock, int maximumBytes)
    {
        _clock = clock;
        _maximumBytes = Math.Clamp(maximumBytes, 1, MaximumTotalBytes);
    }

    internal (int Entries, int Bytes) Usage
    {
        get { lock (_sync) { ReclaimExpired(); return (_entries.Count, _retainedBytes); } }
    }

    internal void Remember(int company, SriSigningCertificateMaterial material)
    {
        if (material.AuxiliaryCertificates.Count > 32) return;
        lock (_sync)
        {
            ReclaimExpired();
            var key = (company, material.Thumbprint);
            _entries.TryGetValue(key, out var old);
            if (old is null && _entries.Count >= 10_000) return;
            // Validation, allocation and reservation share the lock; parallel writers cannot exceed the budget.
            var auxiliary = new List<byte[]>();
            var size = 0;
            foreach (var cert in material.AuxiliaryCertificates)
            {
                var bytes = cert.RawData;
                if (bytes.Length > MaximumCertificateBytes) return;
                size += bytes.Length;
                if (size > MaximumEntryBytes || _retainedBytes - (old?.Size ?? 0) + size > _maximumBytes) return;
                auxiliary.Add(bytes);
            }
            _entries[key] = new(_clock.GetUtcNow(), auxiliary.ToArray(), size);
            _retainedBytes += size - (old?.Size ?? 0);
        }
    }

    internal X509Certificate2[] Read(int company, string thumbprint)
    {
        lock (_sync)
        {
            ReclaimExpired();
            if (!_entries.TryGetValue((company, thumbprint), out var entry)) return [];
            var certificates = new List<X509Certificate2>();
            try
            {
                foreach (var bytes in entry.Auxiliary) certificates.Add(new X509Certificate2(bytes));
                return certificates.ToArray();
            }
            catch { foreach (var cert in certificates) cert.Dispose(); throw; }
        }
    }

    private void ReclaimExpired()
    {
        var cutoff = _clock.GetUtcNow().AddMinutes(-5);
        foreach (var key in _entries.Where(e => e.Value.AsOf <= cutoff).Select(e => e.Key).ToArray())
        {
            _retainedBytes -= _entries[key].Size;
            _entries.Remove(key);
        }
    }
}
