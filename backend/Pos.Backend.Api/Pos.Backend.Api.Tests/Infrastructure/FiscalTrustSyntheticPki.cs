using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Infrastructure;

// Generated exclusively in memory. Never a production trust bootstrap.
internal sealed class FiscalTrustSyntheticPki : IDisposable
{
    public const string RucA = "1790012345001";
    public const string RucB = "1790098765001";
    public DateTime Now { get; } = DateTime.UtcNow;
    private readonly List<X509Certificate2> _owned = [];
    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }
    public X509Certificate2 Leaf { get; }
    public FiscalTrustManifest Manifest { get; }

    public FiscalTrustSyntheticPki(string ruc = RucA, string rootName = "Synthetic RC6 root", byte[]? serial = null)
    {
        Root = Issue(rootName, null, true, 1, "", [1]);
        Intermediate = Issue("Synthetic RC6 intermediate", Root, true, 0, "", [2]);
        Leaf = Issue("Synthetic RC6 taxpayer", Intermediate, false, 0, ruc, serial ?? [3]);
        Manifest = new() { SchemaVersion = 1, Version = "synthetic-rc6-v1", ValidFrom = Now.AddDays(-1), ValidUntil = Now.AddDays(2),
            Authorities = [Authority(Root, "Root"), Authority(Intermediate, "Intermediate")],
            Crls = [Crl(Root), Crl(Intermediate)] };
    }

    public X509Certificate2 Issue(string cn, X509Certificate2? issuer, bool ca, int pathLength, string ruc,
        byte[] serial, X509KeyUsageFlags? usage = null, int keySize = 2048, DateTime? from = null, DateTime? until = null,
        bool omitConstraints = false, bool eku = false, string[]? extendedKeyUsages = null, bool freshestCrl = false)
    {
        using var rsa = RSA.Create(keySize);
        var dn = $"CN={cn},C=EC" + (ruc.Length == 0 ? "" : $",OID.2.5.4.97=VATEC-{ruc}");
        var request = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!omitConstraints) request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, ca, pathLength, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage ?? (ca
            ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign : X509KeyUsageFlags.DigitalSignature), true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        if (issuer is not null)
        {
            var aki = new AsnWriter(AsnEncodingRules.DER);
            aki.PushSequence();
            aki.WriteOctetString(Convert.FromHexString(issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>()
                .Single().SubjectKeyIdentifier!), new Asn1Tag(TagClass.ContextSpecific, 0));
            aki.PopSequence();
            request.CertificateExtensions.Add(new X509Extension("2.5.29.35", aki.Encode(), false));
        }
        // Unreachable tenant-provided URI: deterministic evaluation must never follow it.
        var aia = new AsnWriter(AsnEncodingRules.DER);
        aia.PushSequence(); aia.PushSequence(); aia.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2");
        aia.WriteCharacterString(UniversalTagNumber.IA5String, "http://127.0.0.1:9/tenant-controlled",
            new Asn1Tag(TagClass.ContextSpecific, 6)); aia.PopSequence(); aia.PopSequence();
        request.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.1", aia.Encode(), false));
        if (eku) request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        if (extendedKeyUsages is not null)
        {
            var purposes = new OidCollection();
            foreach (var oid in extendedKeyUsages) purposes.Add(new Oid(oid));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, true));
        }
        if (freshestCrl)
        {
            var points = new AsnWriter(AsnEncodingRules.DER);
            points.PushSequence(); points.PushSequence();
            var name = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            points.PushSequence(name); points.PushSequence(name);
            points.WriteCharacterString(UniversalTagNumber.IA5String, "http://127.0.0.1:9/unsupported-delta",
                new Asn1Tag(TagClass.ContextSpecific, 6));
            points.PopSequence(name); points.PopSequence(name); points.PopSequence(); points.PopSequence();
            request.CertificateExtensions.Add(new X509Extension("2.5.29.46", points.Encode(), false));
        }
        X509Certificate2 cert;
        if (issuer is null) cert = request.CreateSelfSigned(from ?? Now.AddDays(-200), until ?? Now.AddDays(400));
        else
        {
            using var publicCert = request.Create(issuer, from ?? Now.AddDays(-190), until ?? Now.AddDays(300), serial);
            cert = publicCert.CopyWithPrivateKey(rsa);
        }
        _owned.Add(cert);
        return cert;
    }

    public static FiscalAuthority Authority(X509Certificate2 cert, string role) => new()
    { Sha256 = cert.GetCertHashString(HashAlgorithmName.SHA256), CertificateBase64 = Convert.ToBase64String(cert.RawData),
        Role = role, Provenance = "SYNTHETIC in-memory test approval" };

    public FiscalCrlSource Crl(X509Certificate2 issuer, X509Certificate2? revoked = null,
        X509RevocationReason reason = X509RevocationReason.KeyCompromise, DateTime? thisUpdate = null,
        DateTime? nextUpdate = null, string? extraOid = null, bool tamper = false, bool duplicate = false,
        bool futureRevoked = false, string? aki = null)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence(); writer.WriteInteger(1); Algorithm(writer);
        writer.WriteEncodedValue(issuer.SubjectName.RawData);
        writer.WriteUtcTime(thisUpdate ?? Now.AddMinutes(-1)); writer.WriteUtcTime(nextUpdate ?? Now.AddDays(1));
        if (revoked is not null)
        {
            writer.PushSequence();
            for (var i = 0; i < (duplicate ? 2 : 1); i++)
            {
                writer.PushSequence(); writer.WriteIntegerUnsigned(revoked.GetSerialNumber().Reverse().ToArray());
                writer.WriteUtcTime(futureRevoked ? Now.AddHours(1) : Now.AddMinutes(-2));
                writer.PushSequence();
                var value = new AsnWriter(AsnEncodingRules.DER); value.WriteEnumeratedValue(reason);
                Extension(writer, "2.5.29.21", value.Encode());
                writer.PopSequence(); writer.PopSequence();
            }
            writer.PopSequence();
        }
        var tag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        writer.PushSequence(tag); writer.PushSequence();
        var akiValue = new AsnWriter(AsnEncodingRules.DER); akiValue.PushSequence();
        akiValue.WriteOctetString(Convert.FromHexString(aki ?? issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single().SubjectKeyIdentifier!),
            new Asn1Tag(TagClass.ContextSpecific, 0)); akiValue.PopSequence();
        Extension(writer, "2.5.29.35", akiValue.Encode());
        var number = new AsnWriter(AsnEncodingRules.DER); number.WriteInteger(1);
        Extension(writer, "2.5.29.20", number.Encode());
        if (extraOid is not null) Extension(writer, extraOid, number.Encode());
        writer.PopSequence(); writer.PopSequence(tag); writer.PopSequence();
        var tbs = writer.Encode();
        using var rsa = issuer.GetRSAPrivateKey()!;
        var sig = rsa.SignData(tbs, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (tamper) sig[0] ^= 1;
        var outer = new AsnWriter(AsnEncodingRules.DER); outer.PushSequence(); outer.WriteEncodedValue(tbs);
        Algorithm(outer); outer.WriteBitString(sig); outer.PopSequence();
        var der = outer.Encode();
        return new() { IssuerSha256 = issuer.GetCertHashString(HashAlgorithmName.SHA256),
            Sha256 = Convert.ToHexString(SHA256.HashData(der)), DerBase64 = Convert.ToBase64String(der),
            Provenance = "SYNTHETIC full current CRL", MaximumAgeHours = 24,
            ProvisionedAt = Now.AddMinutes(-1), ApprovedCurrentUntil = Now.AddHours(1),
            ApprovalReference = "synthetic-current-publication-approval", CrlNumberHex = "01" };
    }

    private static void Algorithm(AsnWriter writer)
    { writer.PushSequence(); writer.WriteObjectIdentifier("1.2.840.113549.1.1.11"); writer.WriteNull(); writer.PopSequence(); }
    private static void Extension(AsnWriter writer, string oid, byte[] value)
    { writer.PushSequence(); writer.WriteObjectIdentifier(oid); writer.WriteOctetString(value); writer.PopSequence(); }

    public FiscalTrustRegistry Registry()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Manifest);
        return FiscalTrustRegistry.Load(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public byte[] Pfx(params X509Certificate2[] certificates)
    {
        var collection = new X509Certificate2Collection();
        collection.AddRange(certificates);
        return collection.Export(X509ContentType.Pfx, "synthetic-rc6-only")!;
    }
    public void Dispose() { foreach (var cert in _owned) cert.Dispose(); }
}
