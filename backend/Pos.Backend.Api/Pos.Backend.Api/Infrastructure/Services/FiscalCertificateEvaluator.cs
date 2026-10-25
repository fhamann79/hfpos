using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalCertificateEvaluator(FiscalTrustRegistry registry) : IFiscalCertificateEvaluator
{
    private static readonly SemaphoreSlim Evaluations = new(4, 4);

    public FiscalTrustDecision Evaluate(X509Certificate2 signer, IReadOnlyCollection<X509Certificate2> auxiliary,
        string companyRuc, DateTime utcNow)
    {
        var identity = SriCertificateIdentityReader.Read(signer);
        var comparison = identity.Compare(companyRuc);
        var identityState = comparison.Mismatch ? FiscalIdentityState.Mismatch
            : comparison.Match ? FiscalIdentityState.Match : FiscalIdentityState.NotConfirmed;
        var hash = signer.GetCertHashString(HashAlgorithmName.SHA256);
        var valid = signer.NotBefore.ToUniversalTime() <= utcNow && signer.NotAfter.ToUniversalTime() > utcNow;
        var suitable = IsSigner(signer);
        FiscalTrustDecision Result(bool trusted, FiscalEvidenceState revocation, string reason, DateTime? until = null)
            => new(registry.Version, utcNow, until ?? utcNow, hash, trusted, identityState, valid, suitable,
                revocation, reason, identity.Describe());
        if (!Evaluations.Wait(0)) return Result(false, FiscalEvidenceState.Unknown, "FISCAL_TRUST_CAPACITY");
        var certificates = new List<X509Certificate2>();
        try
        {
            var manifest = registry.Manifest;
            if (manifest is null) return Result(false, FiscalEvidenceState.Unknown, registry.Status);
            if (utcNow < manifest.ValidFrom || utcNow >= manifest.ValidUntil)
                return Result(false, FiscalEvidenceState.Stale, "FISCAL_TRUST_REGISTRY_STALE");
            if (auxiliary.Count > 32 || signer.RawData.Length > 32_768)
                return Result(false, FiscalEvidenceState.Error, "FISCAL_CERTIFICATE_LIMIT");
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            // Only chain construction uses NoCheck. Eligibility ALWAYS requires the independent CRL result.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.VerificationTime = utcNow;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1);
            foreach (var a in manifest.Authorities)
            {
                var cert = new X509Certificate2(Convert.FromBase64String(a.CertificateBase64));
                certificates.Add(cert);
                if (a.Role == "Root") chain.ChainPolicy.CustomTrustStore.Add(cert);
                else if (a.Role == "Intermediate") chain.ChainPolicy.ExtraStore.Add(cert);
            }
            foreach (var cert in auxiliary) chain.ChainPolicy.ExtraStore.Add(cert);
            if (!chain.Build(signer) || chain.ChainElements.Count is < 2 or > 10)
                return Result(false, FiscalEvidenceState.Unknown, "CERTIFICATE_CHAIN_BUILD_FAILED");
            var path = chain.ChainElements.Cast<X509ChainElement>().Select(e => e.Certificate).ToArray();
            var suppliedIntermediates = manifest.Authorities.Where(a => a.Role == "Intermediate").Select(a => a.Sha256)
                .Concat(auxiliary.Select(c => c.GetCertHashString(HashAlgorithmName.SHA256))).ToHashSet(StringComparer.Ordinal);
            if (path.Skip(1).SkipLast(1).Any(c => !suppliedIntermediates.Contains(c.GetCertHashString(HashAlgorithmName.SHA256))))
                return Result(false, FiscalEvidenceState.Unknown, "CERTIFICATE_CHAIN_BUILD_FAILED");
            var anchorHash = path[^1].GetCertHashString(HashAlgorithmName.SHA256);
            var profile = manifest.Authorities.SingleOrDefault(a => a.Role == "Root" && a.Sha256 == anchorHash);
            if (profile is null || !VerifyRootSignature(path[^1]) || path.Skip(1).Any(c => !IsAuthority(c)))
                return Result(false, FiscalEvidenceState.Error, "CERTIFICATE_AUTHORITY_INVALID");
            if (path.Any(c => !HasSecureAlgorithm(c))) suitable = false;
            foreach (var oid in profile.RequiredCertificatePolicies) chain.ChainPolicy.CertificatePolicy.Add(new Oid(oid));
            if (profile.RequiredCertificatePolicies.Count > 0 && !chain.Build(signer))
                return Result(false, FiscalEvidenceState.Error, "CERTIFICATE_POLICY_NOT_SUPPORTED");
            if (!path.Select(c => c.GetCertHashString(HashAlgorithmName.SHA256)).SequenceEqual(
                chain.ChainElements.Cast<X509ChainElement>().Select(e => e.Certificate.GetCertHashString(HashAlgorithmName.SHA256))))
                return Result(false, FiscalEvidenceState.Error, "CERTIFICATE_POLICY_NOT_SUPPORTED");
            if (!HasApprovedPurpose(path, profile)) suitable = false;
            if (path.Any(c => c.Extensions.Cast<X509Extension>().Any(e => e.Oid?.Value == "2.5.29.46")))
                return Result(true, FiscalEvidenceState.Error, "CERTIFICATE_REVOCATION_ERROR");
            var authorityHashes = path.Skip(1).Select(c => c.GetCertHashString(HashAlgorithmName.SHA256))
                .ToHashSet(StringComparer.Ordinal);
            if (manifest.Authorities.Any(a => authorityHashes.Contains(a.Sha256) && a.RequiresOcsp))
                return Result(true, FiscalEvidenceState.Unknown, "FISCAL_OCSP_NOT_IMPLEMENTED");
            var validUntil = path.Select(c => c.NotAfter.ToUniversalTime()).Append(manifest.ValidUntil).Min();
            var results = new List<FiscalEvidenceState>();
            for (var i = 0; i < path.Length - 1; i++)
            {
                var issuer = path[i + 1];
                var source = manifest.Crls.SingleOrDefault(c => c.IssuerSha256 == issuer.GetCertHashString(HashAlgorithmName.SHA256));
                if (source is null) { results.Add(FiscalEvidenceState.Unknown); continue; }
                var result = OfflineFiscalCrl.Evaluate(Convert.FromBase64String(source.DerBase64), issuer, path[i], utcNow, source.MaximumAgeHours, source.CrlNumberHex);
                if (source.ProvisionedAt > utcNow || source.ApprovedCurrentUntil <= utcNow
                    || source.ProvisionedAt.AddHours(24) <= utcNow)
                    result = (FiscalEvidenceState.Stale, utcNow);
                results.Add(result.State);
                validUntil = result.Until < validUntil ? result.Until : validUntil;
                validUntil = new[] { validUntil, source.ApprovedCurrentUntil, source.ProvisionedAt.AddHours(24) }.Min();
            }
            var state = new[] { FiscalEvidenceState.Revoked, FiscalEvidenceState.Suspended, FiscalEvidenceState.Error,
                FiscalEvidenceState.Stale, FiscalEvidenceState.Unknown }.FirstOrDefault(results.Contains, FiscalEvidenceState.Good);
            var reason = !valid ? "CERTIFICATE_VALIDITY_INVALID" : !suitable ? "CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED"
                : identityState == FiscalIdentityState.Mismatch ? "CERTIFICATE_RUC_MISMATCH"
                : state != FiscalEvidenceState.Good ? $"CERTIFICATE_REVOCATION_{state.ToString().ToUpperInvariant()}" : "FISCAL_TRUST_GOOD";
            return Result(true, state, reason, validUntil);
        }
        catch (Exception ex) when (ex is CryptographicException or AsnContentException or InvalidOperationException or ArgumentException)
        { return Result(false, FiscalEvidenceState.Error, "FISCAL_TRUST_EVALUATION_ERROR"); }
        finally
        {
            foreach (var cert in certificates) cert.Dispose();
            Evaluations.Release();
        }
    }

    internal static bool IsAuthority(X509Certificate2 cert)
    {
        var constraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        var usage = cert.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        return constraints?.CertificateAuthority == true && usage is not null
            && usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign) && HasSecureAlgorithm(cert);
    }

    private static bool HasApprovedPurpose(IReadOnlyList<X509Certificate2> path, FiscalAuthority profile)
    {
        var purposes = profile.AllowedExtendedKeyUsages.ToHashSet(StringComparer.Ordinal);
        var constrained = false;
        // An absent EKU is unrestricted. Otherwise ONE approved purpose must survive EVERY path constraint.
        foreach (var cert in path)
        {
            var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
            if (eku is null) continue;
            constrained = true;
            var allowed = eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value!).ToHashSet(StringComparer.Ordinal);
            if (allowed.Count == 0) return false;
            if (!allowed.Contains("2.5.29.37.0")) purposes.IntersectWith(allowed);
        }
        return !constrained || purposes.Count > 0;
    }

    private static bool IsSigner(X509Certificate2 cert)
        => cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault()?.CertificateAuthority != true
            && cert.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault() is { } usage
            && usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) && HasSecureAlgorithm(cert);

    private static bool HasSecureAlgorithm(X509Certificate2 cert)
    {
        using var rsa = cert.GetRSAPublicKey();
        return rsa?.KeySize is >= 2048 and <= 8192
            && cert.SignatureAlgorithm.Value is "1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12" or "1.2.840.113549.1.1.13";
    }

    internal static bool VerifyRootSignature(X509Certificate2 cert)
    {
        var reader = new AsnReader(cert.RawData, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        var tbs = sequence.ReadEncodedValue();
        var algorithm = sequence.ReadSequence();
        var oid = algorithm.ReadObjectIdentifier();
        if (algorithm.HasData) algorithm.ReadNull();
        algorithm.ThrowIfNotEmpty();
        var signature = sequence.ReadBitString(out var unused);
        sequence.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
        using var rsa = cert.GetRSAPublicKey();
        var hash = oid switch { "1.2.840.113549.1.1.11" => HashAlgorithmName.SHA256,
            "1.2.840.113549.1.1.12" => HashAlgorithmName.SHA384, "1.2.840.113549.1.1.13" => HashAlgorithmName.SHA512,
            _ => default };
        return unused == 0 && hash != default && rsa is not null && rsa.VerifyData(tbs.Span, signature, hash, RSASignaturePadding.Pkcs1);
    }
}
