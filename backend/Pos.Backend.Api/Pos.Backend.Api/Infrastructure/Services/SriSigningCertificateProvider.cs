using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriSigningCertificateProvider : ISriSigningCertificateProvider
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IDataProtector _protector;
    private readonly ILogger<SriSigningCertificateProvider> _logger;

    public SriSigningCertificateProvider(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SriSigningCertificateProvider> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _protector = dataProtectionProvider.CreateProtector(SriCertificateProtectionPurposes.CertificateV1);
        _logger = logger;
    }

    public async Task<SriSigningCertificateMaterial> GetActiveCertificateMaterialAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var storedCertificate = await _context.CompanySriCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == operationalContext.CompanyId && c.IsActive);

        if (storedCertificate is null)
        {
            throw new KeyNotFoundException("CERTIFICATE_NOT_FOUND");
        }

        if (storedCertificate.NotAfter <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("CERTIFICATE_EXPIRED");
        }

        byte[]? certificateBytes = null;
        byte[]? passwordBytes = null;
        string? password = null;

        try
        {
            try
            {
                certificateBytes = _protector.Unprotect(storedCertificate.EncryptedCertificateBytes);
                passwordBytes = _protector.Unprotect(storedCertificate.EncryptedPassword);
                password = Encoding.UTF8.GetString(passwordBytes);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not unprotect SRI certificate material. CompanyId {CompanyId} CertificateId {CertificateId}",
                    operationalContext.CompanyId,
                    storedCertificate.Id);

                throw new InvalidOperationException("CERTIFICATE_UNPROTECT_FAILED", ex);
            }

            X509Certificate2 certificate;

            try
            {
                certificate = new X509Certificate2(
                    certificateBytes,
                    password,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("CERTIFICATE_LOAD_FAILED", ex);
            }

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
            }

            var notAfter = certificate.NotAfter.ToUniversalTime();

            if (notAfter <= DateTime.UtcNow)
            {
                certificate.Dispose();
                throw new InvalidOperationException("CERTIFICATE_EXPIRED");
            }

            return new SriSigningCertificateMaterial
            {
                CertificateId = storedCertificate.Id,
                Certificate = certificate,
                Thumbprint = certificate.Thumbprint,
                Subject = certificate.Subject,
                SerialNumber = certificate.SerialNumber,
                NotAfter = notAfter
            };
        }
        finally
        {
            if (certificateBytes is not null)
            {
                Array.Clear(certificateBytes);
            }

            if (passwordBytes is not null)
            {
                Array.Clear(passwordBytes);
            }
        }
    }
}
