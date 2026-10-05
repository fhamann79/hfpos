using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hfpos.FiscalSmokeHost;

internal sealed class RecoveryTlsCertificate : IDisposable
{
    internal X509Certificate2 Certificate { get; }

    public RecoveryTlsCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost synthetic HF One", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        // Schannel needs a key container. Without PersistKeySet, .NET deletes this
        // synthetic user key on disposal; no certificate/trust store is installed.
        var flags = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet;
        var pfx = generated.Export(X509ContentType.Pfx);
        try { Certificate = new X509Certificate2(pfx, (string?)null, flags); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    public void Dispose() => Certificate.Dispose();
}
