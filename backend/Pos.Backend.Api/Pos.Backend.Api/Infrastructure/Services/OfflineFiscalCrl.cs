using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Infrastructure.Services;

// Narrow RFC 5280 profile: direct, complete v2 CRLs, RSA PKCS#1 SHA-2, no partitions/deltas.
internal static class OfflineFiscalCrl
{
    internal const int MaximumBytes = 1_048_576;
    private enum Reason { Unspecified, KeyCompromise, CaCompromise, AffiliationChanged, Superseded,
        CessationOfOperation, CertificateHold, Unused, RemoveFromCrl, PrivilegeWithdrawn, AaCompromise }

    internal static (FiscalEvidenceState State, DateTime Until) Evaluate(byte[] der, X509Certificate2 issuer,
        X509Certificate2 subject, DateTime now, int maxAgeHours, string expectedNumber)
    {
        try
        {
            if (der.Length is < 1 or > MaximumBytes) throw new AsnContentException();
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            var list = reader.ReadSequence();
            var tbsBytes = list.ReadEncodedValue();
            var outerAlgorithm = list.ReadEncodedValue();
            var algorithm = ReadAlgorithm(outerAlgorithm);
            var signature = list.ReadBitString(out var unusedBits);
            if (unusedBits != 0) throw new AsnContentException();
            list.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
            using var rsa = issuer.GetRSAPublicKey();
            if (rsa is null || rsa.KeySize is < 2048 or > 8192
                || !rsa.VerifyData(tbsBytes.Span, signature, algorithm, RSASignaturePadding.Pkcs1))
                return (FiscalEvidenceState.Error, now);
            var usage = issuer.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
            if (!FiscalCertificateEvaluator.IsAuthority(issuer) || usage is null
                || !usage.KeyUsages.HasFlag(X509KeyUsageFlags.CrlSign)) return (FiscalEvidenceState.Error, now);
            var tbsReader = new AsnReader(tbsBytes, AsnEncodingRules.DER);
            var tbs = tbsReader.ReadSequence();
            if (tbs.ReadInteger() != 1) throw new AsnContentException();
            var innerAlgorithm = tbs.ReadEncodedValue();
            // Parameters may be absent or NULL, but the algorithms must be identical.
            if (ReadAlgorithm(innerAlgorithm) != algorithm) throw new AsnContentException();
            if (!tbs.ReadEncodedValue().Span.SequenceEqual(issuer.SubjectName.RawData)) throw new AsnContentException();
            var thisUpdate = ReadTime(tbs);
            var nextUpdate = ReadTime(tbs);
            if (nextUpdate <= thisUpdate || thisUpdate > now) return (FiscalEvidenceState.Error, now);
            var until = new[] { nextUpdate, thisUpdate.AddHours(maxAgeHours), issuer.NotAfter.ToUniversalTime() }.Min();
            var serials = new HashSet<string>(StringComparer.Ordinal);
            var state = FiscalEvidenceState.Good;
            var subjectSerial = NormalizeSerial(subject.GetSerialNumber().Reverse().Prepend((byte)0).ToArray());
            if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
            {
                var entries = tbs.ReadSequence();
                while (entries.HasData)
                {
                    if (serials.Count >= 16_384) throw new AsnContentException();
                    var entry = entries.ReadSequence();
                    var serial = NormalizeSerial(entry.ReadIntegerBytes().Span);
                    if (!serials.Add(serial)) throw new AsnContentException();
                    var revokedAt = ReadTime(entry);
                    if (revokedAt > now) throw new AsnContentException();
                    var reason = Reason.Unspecified;
                    if (entry.HasData)
                    {
                        var extensions = entry.ReadSequence();
                        var seen = new HashSet<string>();
                        while (extensions.HasData)
                        {
                            var ext = extensions.ReadSequence();
                            var oid = ext.ReadObjectIdentifier();
                            if (!seen.Add(oid) || seen.Count > 16) throw new AsnContentException();
                            var critical = ext.HasData && ext.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && ext.ReadBoolean();
                            if (critical) throw new AsnContentException();
                            var value = new AsnReader(ext.ReadOctetString(), AsnEncodingRules.DER);
                            ext.ThrowIfNotEmpty();
                            if (oid == "2.5.29.21")
                            {
                                reason = value.ReadEnumeratedValue<Reason>();
                                if (!Enum.IsDefined(reason) || reason is Reason.RemoveFromCrl or Reason.Unused)
                                    throw new AsnContentException();
                                value.ThrowIfNotEmpty();
                            }
                            else if (oid == "2.5.29.24")
                            { if (ReadTime(value) > revokedAt) throw new AsnContentException(); value.ThrowIfNotEmpty(); }
                            else throw new AsnContentException();
                        }
                    }
                    entry.ThrowIfNotEmpty();
                    if (serial == subjectSerial) state = reason == Reason.CertificateHold
                        ? FiscalEvidenceState.Suspended : FiscalEvidenceState.Revoked;
                }
            }
            var wrapper = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
            var crlExtensions = wrapper.ReadSequence();
            var extensionOids = new HashSet<string>();
            var akiMatched = false;
            var numberPresent = false;
            while (crlExtensions.HasData)
            {
                var ext = crlExtensions.ReadSequence();
                var oid = ext.ReadObjectIdentifier();
                if (!extensionOids.Add(oid) || extensionOids.Count > 16) throw new AsnContentException();
                var critical = ext.HasData && ext.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && ext.ReadBoolean();
                if (critical) throw new AsnContentException();
                var value = new AsnReader(ext.ReadOctetString(), AsnEncodingRules.DER);
                ext.ThrowIfNotEmpty();
                if (oid == "2.5.29.35")
                {
                    var aki = value.ReadSequence();
                    var keyId = aki.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
                    aki.ThrowIfNotEmpty(); value.ThrowIfNotEmpty();
                    var ski = issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>().SingleOrDefault()?.SubjectKeyIdentifier;
                    akiMatched = ski is not null && Convert.ToHexString(keyId) == ski;
                }
                else if (oid == "2.5.29.20")
                {
                    numberPresent = NormalizeSerial(value.ReadIntegerBytes().Span) == expectedNumber;
                    value.ThrowIfNotEmpty();
                }
                else throw new AsnContentException();
            }
            wrapper.ThrowIfNotEmpty(); tbs.ThrowIfNotEmpty(); tbsReader.ThrowIfNotEmpty();
            if (!akiMatched || !numberPresent) return (FiscalEvidenceState.Error, now);
            if (until <= now) return (FiscalEvidenceState.Stale, until);
            return (state, until);
        }
        catch (Exception ex) when (ex is AsnContentException or CryptographicException or InvalidOperationException
            or ArgumentException or OverflowException)
        { return (FiscalEvidenceState.Error, now); }
    }

    private static HashAlgorithmName ReadAlgorithm(ReadOnlyMemory<byte> encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        var algorithm = sequence.ReadObjectIdentifier() switch
        {
            "1.2.840.113549.1.1.11" => HashAlgorithmName.SHA256,
            "1.2.840.113549.1.1.12" => HashAlgorithmName.SHA384,
            "1.2.840.113549.1.1.13" => HashAlgorithmName.SHA512,
            _ => throw new AsnContentException()
        };
        if (sequence.HasData) sequence.ReadNull();
        sequence.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
        return algorithm;
    }

    private static DateTime ReadTime(AsnReader reader)
    {
        var encoded = reader.ReadEncodedValue();
        var time = new AsnReader(encoded, AsnEncodingRules.DER);
        var result = encoded.Span[0] switch
        {
            0x17 when encoded.Length == 15 => time.ReadUtcTime(),
            0x18 when encoded.Length == 17 => time.ReadGeneralizedTime(),
            _ => throw new AsnContentException()
        };
        time.ThrowIfNotEmpty();
        return result.UtcDateTime;
    }

    private static string NormalizeSerial(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > 21 || (bytes[0] & 0x80) != 0) throw new AsnContentException();
        while (bytes.Length > 1 && bytes[0] == 0) bytes = bytes[1..];
        if (bytes.Length > 20) throw new AsnContentException();
        return Convert.ToHexString(bytes);
    }
}
