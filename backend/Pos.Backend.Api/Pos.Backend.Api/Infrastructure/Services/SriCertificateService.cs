using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriCertificateService : ISriCertificateService
{
    private const int MaxCertificateFileSizeBytes = 2 * 1024 * 1024;
    private const int MaxFileNameLength = 255;
    private const int MaxContentTypeLength = 100;
    private const int MaxThumbprintLength = 100;
    private const int MaxSubjectLength = 500;
    private const int MaxIssuerLength = 500;
    private const int MaxSerialNumberLength = 100;
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IDataProtector _protector;
    private readonly ILogger<SriCertificateService> _logger;

    public SriCertificateService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SriCertificateService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _protector = dataProtectionProvider.CreateProtector(SriCertificateProtectionPurposes.CertificateV1);
        _logger = logger;
    }

    public async Task<CompanySriCertificateDto> GetCurrentCertificateAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var certificate = await _context.CompanySriCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == operationalContext.CompanyId && c.IsActive);

        if (certificate is null)
        {
            throw new KeyNotFoundException("CERTIFICATE_NOT_FOUND");
        }

        return MapCertificate(certificate);
    }

    public async Task<CompanySriCertificateDto> UploadCertificateAsync(IFormFile? file, string? password)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        ValidateUploadBasics(file, password);

        var certificateBytes = await ReadCertificateBytesAsync(file!);
        var metadata = LoadAndValidateCertificate(certificateBytes, password!);

        byte[] encryptedCertificateBytes;
        byte[] encryptedPassword;

        try
        {
            encryptedCertificateBytes = _protector.Protect(certificateBytes);
            encryptedPassword = _protector.Protect(Encoding.UTF8.GetBytes(password!));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to protect SRI certificate material for company {CompanyId}", operationalContext.CompanyId);
            throw new InvalidOperationException("CERTIFICATE_PROTECTION_FAILED", ex);
        }

        var now = DateTime.UtcNow;
        var fileName = NormalizeMaxLength(Path.GetFileName(file!.FileName), MaxFileNameLength);
        var contentType = NormalizeMaxLength(file.ContentType, MaxContentTypeLength);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var activeCertificates = await _context.CompanySriCertificates
            .Where(c => c.CompanyId == operationalContext.CompanyId && c.IsActive)
            .ToListAsync();

        foreach (var activeCertificate in activeCertificates)
        {
            activeCertificate.IsActive = false;
            activeCertificate.DeactivatedAt = now;
            activeCertificate.DeactivatedByUserId = operationalContext.UserId;
        }

        var certificate = new CompanySriCertificate
        {
            CompanyId = operationalContext.CompanyId,
            FileName = fileName,
            ContentType = contentType,
            EncryptedCertificateBytes = encryptedCertificateBytes,
            EncryptedPassword = encryptedPassword,
            Thumbprint = NormalizeMaxLength(metadata.Thumbprint, MaxThumbprintLength),
            Subject = NormalizeMaxLength(metadata.Subject, MaxSubjectLength),
            Issuer = NormalizeMaxLength(metadata.Issuer, MaxIssuerLength),
            SerialNumber = NormalizeMaxLength(metadata.SerialNumber, MaxSerialNumberLength),
            NotBefore = metadata.NotBefore,
            NotAfter = metadata.NotAfter,
            HasPrivateKey = metadata.HasPrivateKey,
            IsActive = true,
            UploadedAt = now,
            UploadedByUserId = operationalContext.UserId
        };

        _context.CompanySriCertificates.Add(certificate);

        var sriSettings = await GetOrCreateCompanySriSettingsAsync(
            operationalContext.CompanyId,
            operationalContext.UserId,
            now);

        sriSettings.CertificateConfigured = true;
        sriSettings.CertificateExpiresAt = metadata.NotAfter;
        sriSettings.LastUpdatedByUserId = operationalContext.UserId;
        sriSettings.UpdatedAt = now;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapCertificate(certificate);
    }

    public async Task DeactivateCertificateAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var certificate = await _context.CompanySriCertificates
            .FirstOrDefaultAsync(c => c.CompanyId == operationalContext.CompanyId && c.IsActive);

        if (certificate is null)
        {
            throw new KeyNotFoundException("CERTIFICATE_NOT_FOUND");
        }

        certificate.IsActive = false;
        certificate.DeactivatedAt = now;
        certificate.DeactivatedByUserId = operationalContext.UserId;

        var sriSettings = await GetOrCreateCompanySriSettingsAsync(
            operationalContext.CompanyId,
            operationalContext.UserId,
            now);

        sriSettings.CertificateConfigured = false;
        sriSettings.CertificateExpiresAt = null;
        sriSettings.LastUpdatedByUserId = operationalContext.UserId;
        sriSettings.UpdatedAt = now;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateUploadBasics(IFormFile? file, string? password)
    {
        if (file is null || file.Length == 0)
        {
            throw new InvalidOperationException("CERTIFICATE_FILE_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("CERTIFICATE_PASSWORD_REQUIRED");
        }

        if (file.Length > MaxCertificateFileSizeBytes)
        {
            throw new InvalidOperationException("INVALID_CERTIFICATE_FILE");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (extension is not ".p12" and not ".pfx")
        {
            throw new InvalidOperationException("INVALID_CERTIFICATE_FILE");
        }
    }

    private static async Task<byte[]> ReadCertificateBytesAsync(IFormFile file)
    {
        await using var memoryStream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memoryStream);

        var certificateBytes = memoryStream.ToArray();

        if (certificateBytes.Length == 0 || certificateBytes.Length > MaxCertificateFileSizeBytes)
        {
            throw new InvalidOperationException("INVALID_CERTIFICATE_FILE");
        }

        return certificateBytes;
    }

    private static CertificateMetadata LoadAndValidateCertificate(byte[] certificateBytes, string password)
    {
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
            throw new InvalidOperationException("INVALID_CERTIFICATE_PASSWORD", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("INVALID_CERTIFICATE_FILE", ex);
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
            {
                throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
            }

            var notBefore = certificate.NotBefore.ToUniversalTime();
            var notAfter = certificate.NotAfter.ToUniversalTime();
            var now = DateTime.UtcNow;

            if (notAfter <= now)
            {
                throw new InvalidOperationException("CERTIFICATE_EXPIRED");
            }

            if (notBefore > now)
            {
                throw new InvalidOperationException("CERTIFICATE_NOT_VALID_YET");
            }

            return new CertificateMetadata(
                certificate.Thumbprint,
                certificate.Subject,
                certificate.Issuer,
                certificate.SerialNumber,
                notBefore,
                notAfter,
                certificate.HasPrivateKey);
        }
    }

    private async Task<CompanySriSettings> GetOrCreateCompanySriSettingsAsync(int companyId, int userId, DateTime now)
    {
        var settings = await _context.CompanySriSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings is not null)
        {
            return settings;
        }

        settings = new CompanySriSettings
        {
            CompanyId = companyId,
            Environment = 1,
            EmissionType = 1,
            IsEnabled = false,
            CertificateConfigured = false,
            LastUpdatedByUserId = userId,
            CreatedAt = now
        };

        _context.CompanySriSettings.Add(settings);

        return settings;
    }

    private static CompanySriCertificateDto MapCertificate(CompanySriCertificate certificate)
    {
        var now = DateTime.UtcNow;
        var daysUntilExpiration = (int)Math.Ceiling((certificate.NotAfter - now).TotalDays);

        return new CompanySriCertificateDto
        {
            CompanyId = certificate.CompanyId,
            CertificateConfigured = certificate.IsActive,
            FileName = certificate.FileName,
            Thumbprint = certificate.Thumbprint,
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            SerialNumber = certificate.SerialNumber,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            HasPrivateKey = certificate.HasPrivateKey,
            UploadedAt = certificate.UploadedAt,
            UploadedByUserId = certificate.UploadedByUserId,
            IsActive = certificate.IsActive,
            DaysUntilExpiration = Math.Max(0, daysUntilExpiration),
            IsExpired = certificate.NotAfter <= now
        };
    }

    private static string NormalizeMaxLength(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed record CertificateMetadata(
        string Thumbprint,
        string Subject,
        string Issuer,
        string SerialNumber,
        DateTime NotBefore,
        DateTime NotAfter,
        bool HasPrivateKey);
}
