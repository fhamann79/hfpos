using System.Security.Cryptography.X509Certificates;

namespace Pos.Backend.Api.Core.Services;

public interface ISriXadesBesSigner
{
    string SignInvoiceXml(
        string unsignedXml,
        X509Certificate2 certificate,
        string accessKey,
        DateTime signingTimeUtc);
}
