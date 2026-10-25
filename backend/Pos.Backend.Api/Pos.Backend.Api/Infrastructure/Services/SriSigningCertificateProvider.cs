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
        return await GetForCompanyAsync(operationalContext.CompanyId);
    }

    internal Task<SriSigningCertificateMaterial> GetForAdmissionAsync(ElectronicIssuingCoordinator.Admission authority)
        => GetForCompanyAsync(authority.Sale.CompanyId);

    internal Task<SriSigningCertificateMaterial> GetForCreationAsync(OperationalContext authority)
        => GetForCompanyAsync(authority.CompanyId);

    private async Task<SriSigningCertificateMaterial> GetForCompanyAsync(int companyId)
    {

        var storedCertificate = await _context.CompanySriCertificates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync();

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
                    companyId,
                    storedCertificate.Id);

                throw new InvalidOperationException("CERTIFICATE_UNPROTECT_FAILED", ex);
            }

            SriSigningCertificateMaterial material;

            try
            {
                material = SriPfxReader.Load(certificateBytes, password, storedCertificate.Id);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("CERTIFICATE_LOAD_FAILED", ex);
            }

            var notAfter = material.Certificate.NotAfter.ToUniversalTime();

            if (notAfter <= DateTime.UtcNow)
            {
                material.Dispose();
                throw new InvalidOperationException("CERTIFICATE_EXPIRED");
            }
            if (material.Certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            {
                material.Dispose();
                throw new InvalidOperationException("CERTIFICATE_NOT_YET_VALID");
            }

            return material;
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
