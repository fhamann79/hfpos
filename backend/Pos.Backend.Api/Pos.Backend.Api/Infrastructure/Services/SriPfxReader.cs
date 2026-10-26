using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class SriPfxReader
{
    private static readonly SemaphoreSlim Imports = new(4, 4);
    public static SriSigningCertificateMaterial Load(byte[] bytes, string password, int certificateId = 0)
    {
        if (!Imports.Wait(0)) throw new InvalidOperationException("CERTIFICATE_IMPORT_CAPACITY");
        var collection = new X509Certificate2Collection();
        try
        {
            if (bytes.Length is < 1 or > 2_097_152) throw new CryptographicException();
            collection.Import(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
            var signers = collection.Cast<X509Certificate2>().Where(c => c.HasPrivateKey).ToArray();
            if (collection.Count > 33) throw new InvalidOperationException("CERTIFICATE_COLLECTION_LIMIT");
            if (signers.Length == 0) throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
            if (signers.Length != 1) throw new InvalidOperationException("CERTIFICATE_SIGNER_AMBIGUOUS");
            var cert = signers[0];
            foreach (var auxiliary in collection.Cast<X509Certificate2>().Where(c => c != cert)) auxiliary.Dispose();
            return new SriSigningCertificateMaterial
            {
                CertificateId = certificateId, Certificate = cert, Thumbprint = cert.Thumbprint,
                Subject = cert.Subject, SerialNumber = cert.SerialNumber, NotAfter = cert.NotAfter.ToUniversalTime()
            };
        }
        catch
        {
            foreach (var cert in collection) cert.Dispose();
            throw;
        }
        finally { Imports.Release(); }
    }
}
