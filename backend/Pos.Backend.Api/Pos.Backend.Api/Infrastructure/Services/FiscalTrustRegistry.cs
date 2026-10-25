using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalTrustManifest
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public List<FiscalAuthority> Authorities { get; set; } = [];
    public List<FiscalCrlSource> Crls { get; set; } = [];
}

public sealed class FiscalAuthority
{
    public string Sha256 { get; set; } = string.Empty;
    public string CertificateBase64 { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string Profile { get; set; } = "rsa-full-crl-v1";
    public bool RequiresOcsp { get; set; }
    public List<string> AllowedExtendedKeyUsages { get; set; } = [];
    public List<string> RequiredCertificatePolicies { get; set; } = [];
}

public sealed class FiscalCrlSource
{
    public string IssuerSha256 { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string DerBase64 { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public int MaximumAgeHours { get; set; } = 24;
    public DateTime ProvisionedAt { get; set; }
    public DateTime ApprovedCurrentUntil { get; set; }
    public string ApprovalReference { get; set; } = string.Empty;
    public string CrlNumberHex { get; set; } = string.Empty;
}

// The expected digest is an independent operational approval, never derived from the input here.
public sealed class FiscalTrustRegistry
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    internal FiscalTrustManifest? Manifest { get; private init; }
    public string Version => Manifest?.Version ?? "UNAVAILABLE";
    public string Status { get; private init; } = "FISCAL_TRUST_REGISTRY_UNAVAILABLE";
    public bool Available => Manifest is not null;
    public DateTime? ValidFrom => Manifest?.ValidFrom;
    public DateTime? ValidUntil => Manifest?.ValidUntil;
    public string StatusAt(DateTime now) => Manifest is { } manifest && (now < manifest.ValidFrom || now >= manifest.ValidUntil)
        ? "FISCAL_TRUST_REGISTRY_STALE" : Status;
    public IReadOnlyList<object> PublicAuthorities => Manifest?.Authorities.Select(a => (object)new
        { a.Sha256, a.Role, a.Provenance, a.Profile, a.RequiresOcsp }).ToArray() ?? [];
    public IReadOnlyList<object> PublicRevocationSources => Manifest?.Crls.Select(c => (object)new
        { c.IssuerSha256, c.Sha256, c.CrlNumberHex, c.Provenance, c.ProvisionedAt, c.ApprovedCurrentUntil, c.MaximumAgeHours }).ToArray() ?? [];

    public FiscalTrustRegistry(IOptions<FiscalTrustOptions> options)
    {
        try
        {
            var file = new FileInfo(options.Value.RegistryPath);
            if (!file.Exists || file.Length is < 1 or > MaximumBytes) return;
            var loaded = Load(File.ReadAllBytes(file.FullName), options.Value.ExpectedSha256);
            Manifest = loaded.Manifest;
            Status = loaded.Status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = "FISCAL_TRUST_REGISTRY_UNREADABLE"; }
    }

    private FiscalTrustRegistry() { }

    public static FiscalTrustRegistry Load(byte[] bytes, string approvedSha256)
    {
        try
        {
            if (bytes.Length is < 1 or > MaximumBytes || approvedSha256.Length != 64
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(approvedSha256)))
                return new() { Status = "FISCAL_TRUST_REGISTRY_NOT_APPROVED" };
            var manifest = JsonSerializer.Deserialize<FiscalTrustManifest>(bytes, new JsonSerializerOptions
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 })
                ?? throw new InvalidDataException();
            if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 100
                || manifest.ValidFrom.Kind != DateTimeKind.Utc || manifest.ValidUntil.Kind != DateTimeKind.Utc
                || manifest.ValidUntil <= manifest.ValidFrom || manifest.Authorities.Count is < 1 or > 64
                || manifest.Crls.Count > 64 || manifest.Authorities.Select(a => a.Sha256).Distinct().Count() != manifest.Authorities.Count
                || manifest.Crls.Select(c => c.IssuerSha256).Distinct().Count() != manifest.Crls.Count)
                throw new InvalidDataException();
            foreach (var a in manifest.Authorities)
            {
                var der = Convert.FromBase64String(a.CertificateBase64);
                if (der.Length > 32_768 || a.Provenance.Length is < 1 or > 1000
                    || a.Role is not ("Root" or "Intermediate" or "CrlIssuer") || a.Profile != "rsa-full-crl-v1"
                    || a.Sha256 != Convert.ToHexString(SHA256.HashData(der))
                    || a.AllowedExtendedKeyUsages.Count > 16 || a.RequiredCertificatePolicies.Count > 16)
                    throw new InvalidDataException();
                if (a.Role != "Root" && (a.AllowedExtendedKeyUsages.Count > 0 || a.RequiredCertificatePolicies.Count > 0))
                    throw new InvalidDataException(); // These two profile lists are root-only, never silently ignored.
                using var cert = new X509Certificate2(der);
                if (cert.HasPrivateKey || !FiscalCertificateEvaluator.IsAuthority(cert)) throw new InvalidDataException();
                if (a.Role == "Root" && (!cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData)
                    || !FiscalCertificateEvaluator.VerifyRootSignature(cert)))
                    throw new InvalidDataException();
            }
            if (!manifest.Authorities.Any(a => a.Role == "Root")) throw new InvalidDataException();
            foreach (var c in manifest.Crls)
            {
                var der = Convert.FromBase64String(c.DerBase64);
                if (der.Length is < 1 or > OfflineFiscalCrl.MaximumBytes || c.MaximumAgeHours is < 1 or > 8784
                    || c.Provenance.Length is < 1 or > 1000 || c.Sha256 != Convert.ToHexString(SHA256.HashData(der))
                    || c.ProvisionedAt.Kind != DateTimeKind.Utc || c.ApprovedCurrentUntil.Kind != DateTimeKind.Utc
                    || c.ApprovedCurrentUntil <= c.ProvisionedAt || c.ApprovalReference.Length is < 1 or > 500
                    || c.ApprovedCurrentUntil > c.ProvisionedAt.AddHours(24) || c.CrlNumberHex.Length is < 2 or > 40
                    || !manifest.Authorities.Any(a => a.Sha256 == c.IssuerSha256)) throw new InvalidDataException();
            }
            return new() { Manifest = manifest, Status = "AVAILABLE" };
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or InvalidDataException
            or NullReferenceException or ArgumentException)
        { return new() { Status = "FISCAL_TRUST_REGISTRY_INVALID" }; }
    }
}
