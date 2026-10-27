using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pos.Backend.Api.Tests.Infrastructure;

// Synthetic authorities are never installed in a trust store or shipped with the API.
internal sealed class SyntheticSigningCertificates : IDisposable
{
    public const string RucA = "1790012345001";
    public const string RucB = "1790098765001";
    public const string Password = "synthetic-rc6-only";
    public DateTime Now { get; } = DateTime.UtcNow;
    private readonly List<X509Certificate2> _owned = [];
    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }
    public X509Certificate2 Leaf { get; }

    public SyntheticSigningCertificates(string ruc = RucA, string rootName = "Synthetic RC6 root")
    {
        Root = Issue(rootName, ca: true);
        Intermediate = Issue("Synthetic RC6 intermediate", Root, ca: true);
        Leaf = Issue("Synthetic RC6 taxpayer", Intermediate, ruc: ruc);
    }

    public X509Certificate2 Issue(string cn, X509Certificate2? issuer = null, bool ca = false,
        string ruc = "", int keySize = 2048, DateTime? from = null, DateTime? until = null)
    {
        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest($"CN={cn},C=EC" + (ruc.Length == 0 ? "" : $",OID.2.5.4.97=VATEC-{ruc}"),
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(ca
            ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign : X509KeyUsageFlags.DigitalSignature, true));
        // An unreachable tenant-supplied AIA must never be followed by readiness.
        var aia = new AsnWriter(AsnEncodingRules.DER);
        aia.PushSequence(); aia.PushSequence(); aia.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2");
        aia.WriteCharacterString(UniversalTagNumber.IA5String, "http://127.0.0.1:9/tenant-controlled",
            new Asn1Tag(TagClass.ContextSpecific, 6)); aia.PopSequence(); aia.PopSequence();
        request.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.1", aia.Encode(), false));
        X509Certificate2 cert;
        if (issuer is null) cert = request.CreateSelfSigned(from ?? Now.AddDays(-200), until ?? Now.AddDays(400));
        else
        {
            using var publicCert = request.Create(issuer, from ?? Now.AddDays(-190), until ?? Now.AddDays(300), RandomNumberGenerator.GetBytes(16));
            cert = publicCert.CopyWithPrivateKey(rsa);
        }
        _owned.Add(cert);
        return cert;
    }

    public byte[] Pfx(params X509Certificate2[] certificates)
    {
        var collection = new X509Certificate2Collection();
        collection.AddRange(certificates);
        return collection.Export(X509ContentType.Pfx, Password)!;
    }

    public void Dispose() { foreach (var cert in _owned) cert.Dispose(); }
}
