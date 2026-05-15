using System.Security.Cryptography.X509Certificates;

namespace Pos.Backend.Api.Core.Models;

public sealed class SriSigningCertificateMaterial : IDisposable
{
    public int CertificateId { get; init; }

    public X509Certificate2 Certificate { get; init; } = null!;

    public string Thumbprint { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public DateTime NotAfter { get; init; }

    public void Dispose()
    {
        Certificate.Dispose();
    }
}
