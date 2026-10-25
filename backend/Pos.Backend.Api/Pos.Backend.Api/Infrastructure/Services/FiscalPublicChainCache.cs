using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalPublicChainCache
{
    private sealed record Entry(DateTime AsOf, byte[][] Auxiliary);
    private readonly ConcurrentDictionary<(int Company, string Thumbprint), Entry> _entries = new();

    internal void Remember(int company, SriSigningCertificateMaterial material)
    {
        if (_entries.Count >= 10_000 && !_entries.ContainsKey((company, material.Thumbprint))) return;
        _entries[(company, material.Thumbprint)] = new(DateTime.UtcNow,
            material.AuxiliaryCertificates.Select(c => c.RawData).ToArray());
    }

    internal X509Certificate2[] Read(int company, string thumbprint)
        => _entries.TryGetValue((company, thumbprint), out var entry) && entry.AsOf > DateTime.UtcNow.AddMinutes(-5)
            ? entry.Auxiliary.Select(b => new X509Certificate2(b)).ToArray() : [];
}
