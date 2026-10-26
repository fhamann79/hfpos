using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalDocumentGuard(PosDbContext db, ISriSigningCertificateProvider provider)
{
    internal async Task BeforeCreateProductionAsync(OperationalContext authority)
    {
        using var material = provider is SriSigningCertificateProvider concrete
            ? await concrete.GetForCreationAsync(authority) : throw new InvalidOperationException("FISCAL_CERTIFICATE_PROVIDER_REQUIRED");
        var ruc = await db.Companies.AsNoTracking().Where(c => c.Id == authority.CompanyId).Select(c => c.Ruc).SingleAsync();
        RequireSigningCertificate(material.Certificate, ruc, DateTime.UtcNow);
    }

    internal static void RequireSigningCertificate(X509Certificate2 certificate, string ruc, DateTime now)
    {
        RequirePublicCertificate(certificate, ruc, now);
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
        try
        {
            using var privateKey = certificate.GetRSAPrivateKey();
            using var publicKey = certificate.GetRSAPublicKey();
            if (privateKey is null || publicKey is null)
                throw new InvalidOperationException("CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED");
            VerifyKeyPair(privateKey, publicKey);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("CERTIFICATE_PRIVATE_KEY_UNUSABLE");
        }
    }

    internal static void VerifyKeyPair(RSA privateKey, RSA publicKey)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = privateKey.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!publicKey.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidOperationException("CERTIFICATE_PRIVATE_KEY_MISMATCH");
    }

    private static void RequirePublicCertificate(X509Certificate2 certificate, string ruc, DateTime now)
    {
        if (certificate.NotBefore.ToUniversalTime() > now) throw new InvalidOperationException("CERTIFICATE_NOT_YET_VALID");
        if (certificate.NotAfter.ToUniversalTime() <= now) throw new InvalidOperationException("CERTIFICATE_EXPIRED");
        using var key = certificate.GetRSAPublicKey();
        if (key?.KeySize is not (>= 2048 and <= 8192))
            throw new InvalidOperationException("CERTIFICATE_SIGNING_PROFILE_UNSUPPORTED");
        if (SriCertificateIdentityReader.Read(certificate).Compare(ruc).Mismatch)
            throw new InvalidOperationException("CERTIFICATE_RUC_MISMATCH");
    }

    internal static void BeforeReception(Sale sale)
    {
        var issuer = FiscalSnapshots.Read(sale.FiscalIssuerSnapshotJson)
            ?? throw new InvalidOperationException("FISCAL_LEGACY_REVIEW_REQUIRED");
        using var cert = FiscalSignedXmlVerifier.Verify(sale.SriSignedXml ?? "", sale.SriSignatureHash ?? "",
            sale.SriSigningCertificateThumbprint ?? "", sale.AccessKey ?? "", issuer.Ruc, "factura", sale.SriEnvironment ?? 0);
        RequirePublicCertificate(cert, issuer.Ruc, DateTime.UtcNow);
    }

    internal static void BeforeCreditNoteReception(CreditNote note, int environment)
    {
        var issuer = FiscalSnapshots.Read(note.FiscalIssuerSnapshotJson)
            ?? throw new InvalidOperationException("FISCAL_LEGACY_REVIEW_REQUIRED");
        using var cert = FiscalSignedXmlVerifier.Verify(note.SriSignedXml ?? "", note.SriSignatureHash ?? "",
            note.SriSigningCertificateThumbprint ?? "", note.AccessKey ?? "", issuer.Ruc, "notaCredito", environment);
        RequirePublicCertificate(cert, issuer.Ruc, DateTime.UtcNow);
    }
}
