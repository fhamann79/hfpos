using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class FiscalSettingsService : IFiscalSettingsService
{
    private const int MaxLogoFileSizeBytes = 512 * 1024;

    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;

    public FiscalSettingsService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
    }

    public async Task<CompanyFiscalSettingsDto> GetCompanyFiscalSettingsAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var company = await GetCompanyAsync(operationalContext.CompanyId);

        return MapCompany(company);
    }

    public async Task<CompanyFiscalSettingsDto> UpdateCompanyFiscalSettingsAsync(UpdateCompanyFiscalSettingsDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var company = await GetCompanyAsync(operationalContext.CompanyId);

        var name = dto.Name?.Trim();
        var ruc = dto.Ruc?.Trim();
        var tradeName = NormalizeOptional(dto.TradeName);
        var matrixAddress = NormalizeOptional(dto.MatrixAddress);
        var email = NormalizeOptional(dto.Email);
        var phone = NormalizeOptional(dto.Phone);
        var specialTaxpayerNumber = NormalizeOptional(dto.SpecialTaxpayerNumber);
        var taxpayerRegime = NormalizeOptional(dto.TaxpayerRegime);

        if (!IsNumeric(ruc, 13))
        {
            throw new InvalidOperationException("INVALID_COMPANY_RUC");
        }

        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 150
            || tradeName?.Length > 150
            || matrixAddress?.Length > 250
            || email?.Length > 150
            || phone?.Length > 30
            || specialTaxpayerNumber?.Length > 50
            || taxpayerRegime?.Length > 80
            || !IsValidBasicEmail(email))
        {
            throw new InvalidOperationException("INVALID_COMPANY_FISCAL_SETTINGS");
        }

        company.Name = name!;
        company.Ruc = ruc!;
        company.TradeName = tradeName;
        company.MatrixAddress = matrixAddress;
        company.Email = email;
        company.Phone = phone;
        company.IsAccountingRequired = dto.IsAccountingRequired;
        company.SpecialTaxpayerNumber = specialTaxpayerNumber;
        company.TaxpayerRegime = taxpayerRegime;

        await _context.SaveChangesAsync();

        return MapCompany(company);
    }

    public async Task<CompanyBrandingDto> GetCompanyBrandingAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        await GetCompanyAsync(operationalContext.CompanyId);

        var branding = await _context.CompanyBrandings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == operationalContext.CompanyId);

        return MapBranding(operationalContext.CompanyId, branding);
    }

    public async Task<CompanyBrandingDto> UpdateCompanyBrandingAsync(UpdateCompanyBrandingDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var primaryColor = NormalizeOptional(dto.PrimaryColor);
        var documentFooterText = NormalizeOptional(dto.DocumentFooterText);

        if (!IsValidBrandingColor(primaryColor)
            || primaryColor?.Length > 20
            || documentFooterText?.Length > 500)
        {
            throw new InvalidOperationException("COMPANY_BRANDING_OPERATION_FAILED");
        }

        var branding = await GetOrCreateCompanyBrandingAsync(operationalContext.CompanyId, operationalContext.UserId);
        var now = DateTime.UtcNow;

        branding.PrimaryColor = primaryColor;
        branding.DocumentFooterText = documentFooterText;
        branding.UpdatedAt = now;
        branding.UpdatedByUserId = operationalContext.UserId;

        await _context.SaveChangesAsync();

        return MapBranding(branding.CompanyId, branding);
    }

    public async Task<CompanyBrandingDto> UploadCompanyLogoAsync(IFormFile? file)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        if (file is null)
        {
            throw new InvalidOperationException("COMPANY_LOGO_FILE_REQUIRED");
        }

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("COMPANY_LOGO_INVALID");
        }

        if (file.Length > MaxLogoFileSizeBytes)
        {
            throw new InvalidOperationException("COMPANY_LOGO_TOO_LARGE");
        }

        var contentType = NormalizeLogoContentType(file.ContentType);

        if (contentType is null)
        {
            throw new InvalidOperationException("COMPANY_LOGO_UNSUPPORTED_TYPE");
        }

        byte[] logoBytes;

        await using (var stream = file.OpenReadStream())
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            logoBytes = memory.ToArray();
        }

        if (logoBytes.Length == 0 || logoBytes.LongLength > MaxLogoFileSizeBytes || !HasValidLogoSignature(logoBytes, contentType))
        {
            throw new InvalidOperationException("COMPANY_LOGO_INVALID");
        }

        var branding = await GetOrCreateCompanyBrandingAsync(operationalContext.CompanyId, operationalContext.UserId);
        var now = DateTime.UtcNow;

        branding.LogoBytes = logoBytes;
        branding.LogoContentType = contentType;
        branding.LogoFileName = TruncateFileName(file.FileName);
        branding.LogoSizeBytes = logoBytes.LongLength;
        branding.LogoUpdatedAt = now;
        branding.UpdatedAt = now;
        branding.UpdatedByUserId = operationalContext.UserId;

        await _context.SaveChangesAsync();

        return MapBranding(branding.CompanyId, branding);
    }

    public async Task<CompanyLogoFileResult> GetCompanyLogoAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var branding = await _context.CompanyBrandings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == operationalContext.CompanyId);

        if (branding?.LogoBytes is not { Length: > 0 }
            || string.IsNullOrWhiteSpace(branding.LogoContentType))
        {
            throw new KeyNotFoundException("COMPANY_LOGO_NOT_FOUND");
        }

        return new CompanyLogoFileResult
        {
            Bytes = branding.LogoBytes,
            ContentType = branding.LogoContentType,
            FileName = branding.LogoFileName ?? "company-logo"
        };
    }

    public async Task DeleteCompanyLogoAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var branding = await _context.CompanyBrandings
            .FirstOrDefaultAsync(b => b.CompanyId == operationalContext.CompanyId);

        if (branding is null)
        {
            return;
        }

        branding.LogoBytes = null;
        branding.LogoContentType = null;
        branding.LogoFileName = null;
        branding.LogoSizeBytes = null;
        branding.LogoUpdatedAt = null;
        branding.UpdatedAt = DateTime.UtcNow;
        branding.UpdatedByUserId = operationalContext.UserId;

        await _context.SaveChangesAsync();
    }

    public async Task<CompanySriSettingsDto> GetCompanySriSettingsAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var settings = await GetOrCreateCompanySriSettingsAsync(operationalContext.CompanyId, operationalContext.UserId);

        return MapSriSettings(settings);
    }

    public async Task<CompanySriSettingsDto> UpdateCompanySriSettingsAsync(UpdateCompanySriSettingsDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        ValidateSriSettings(dto.Environment, dto.EmissionType);

        var settings = await GetOrCreateCompanySriSettingsAsync(operationalContext.CompanyId, operationalContext.UserId);
        settings.Environment = dto.Environment;
        settings.EmissionType = dto.EmissionType;
        settings.IsEnabled = dto.IsEnabled;
        settings.LastUpdatedByUserId = operationalContext.UserId;
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapSriSettings(settings);
    }

    public async Task<IReadOnlyList<DocumentSequenceDto>> GetDocumentSequencesAsync(
        int? establishmentId,
        int? emissionPointId,
        SaleDocumentType? documentType)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var query = _context.DocumentSequences
            .AsNoTracking()
            .Include(s => s.Establishment)
            .Include(s => s.EmissionPoint)
            .Where(s => s.CompanyId == operationalContext.CompanyId);

        if (establishmentId.HasValue)
        {
            query = query.Where(s => s.EstablishmentId == establishmentId.Value);
        }

        if (emissionPointId.HasValue)
        {
            query = query.Where(s => s.EmissionPointId == emissionPointId.Value);
        }

        if (documentType.HasValue)
        {
            if (!Enum.IsDefined(documentType.Value))
            {
                throw new InvalidOperationException("INVALID_DOCUMENT_SEQUENCE");
            }

            query = query.Where(s => s.DocumentType == documentType.Value);
        }

        var sequences = await query
            .OrderBy(s => s.Establishment.Code)
            .ThenBy(s => s.EmissionPoint.Code)
            .ThenBy(s => s.DocumentType)
            .ToListAsync();

        var result = new List<DocumentSequenceDto>();

        foreach (var sequence in sequences)
        {
            var maxUsed = await GetMaxUsedSequentialAsync(
                operationalContext.CompanyId,
                sequence.EstablishmentId,
                sequence.EmissionPointId,
                sequence.DocumentType);

            result.Add(MapDocumentSequence(sequence, maxUsed));
        }

        return result;
    }

    public async Task<DocumentSequenceDto> CreateDocumentSequenceAsync(CreateDocumentSequenceDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var reason = ValidateReason(dto.Reason);
        ValidateDocumentType(dto.DocumentType);
        ValidateNextNumber(dto.NextNumber);

        await ValidateOperationalStructureAsync(
            operationalContext.CompanyId,
            dto.EstablishmentId,
            dto.EmissionPointId);

        var exists = await _context.DocumentSequences
            .AnyAsync(s =>
                s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == dto.EstablishmentId
                && s.EmissionPointId == dto.EmissionPointId
                && s.DocumentType == dto.DocumentType);

        if (exists)
        {
            throw new InvalidOperationException("DOCUMENT_SEQUENCE_ALREADY_EXISTS");
        }

        var newCurrentNumber = dto.NextNumber - 1;
        var maxUsed = await GetMaxUsedSequentialAsync(
            operationalContext.CompanyId,
            dto.EstablishmentId,
            dto.EmissionPointId,
            dto.DocumentType);

        if (newCurrentNumber < maxUsed)
        {
            throw new InvalidOperationException("DOCUMENT_SEQUENCE_BELOW_USED_NUMBER");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var now = DateTime.UtcNow;
        var sequence = new DocumentSequence
        {
            CompanyId = operationalContext.CompanyId,
            EstablishmentId = dto.EstablishmentId,
            EmissionPointId = dto.EmissionPointId,
            DocumentType = dto.DocumentType,
            CurrentNumber = newCurrentNumber,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.DocumentSequences.Add(sequence);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            throw new InvalidOperationException("DOCUMENT_SEQUENCE_ALREADY_EXISTS", ex);
        }

        _context.DocumentSequenceAudits.Add(new DocumentSequenceAudit
        {
            DocumentSequenceId = sequence.Id,
            CompanyId = sequence.CompanyId,
            EstablishmentId = sequence.EstablishmentId,
            EmissionPointId = sequence.EmissionPointId,
            DocumentType = sequence.DocumentType,
            PreviousCurrentNumber = null,
            NewCurrentNumber = sequence.CurrentNumber,
            PreviousNextNumber = null,
            NewNextNumber = sequence.CurrentNumber + 1,
            Reason = reason,
            UserId = operationalContext.UserId,
            CreatedAt = now
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetDocumentSequenceDtoAsync(sequence.Id, operationalContext.CompanyId);
    }

    public async Task<DocumentSequenceDto> UpdateDocumentSequenceAsync(int id, UpdateDocumentSequenceDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var reason = ValidateReason(dto.Reason);
        ValidateNextNumber(dto.NextNumber);

        var sequence = await _context.DocumentSequences
            .Include(s => s.Establishment)
            .Include(s => s.EmissionPoint)
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == operationalContext.CompanyId);

        if (sequence is null)
        {
            throw new KeyNotFoundException("DOCUMENT_SEQUENCE_NOT_FOUND");
        }

        var newCurrentNumber = dto.NextNumber - 1;
        var maxUsed = await GetMaxUsedSequentialAsync(
            sequence.CompanyId,
            sequence.EstablishmentId,
            sequence.EmissionPointId,
            sequence.DocumentType);

        if (newCurrentNumber < maxUsed || newCurrentNumber < sequence.CurrentNumber)
        {
            throw new InvalidOperationException("DOCUMENT_SEQUENCE_BELOW_USED_NUMBER");
        }

        if (newCurrentNumber == sequence.CurrentNumber)
        {
            return MapDocumentSequence(sequence, maxUsed);
        }

        var now = DateTime.UtcNow;
        var previousCurrent = sequence.CurrentNumber;
        var previousNext = sequence.CurrentNumber + 1;

        sequence.CurrentNumber = newCurrentNumber;
        sequence.UpdatedAt = now;

        _context.DocumentSequenceAudits.Add(new DocumentSequenceAudit
        {
            DocumentSequenceId = sequence.Id,
            CompanyId = sequence.CompanyId,
            EstablishmentId = sequence.EstablishmentId,
            EmissionPointId = sequence.EmissionPointId,
            DocumentType = sequence.DocumentType,
            PreviousCurrentNumber = previousCurrent,
            NewCurrentNumber = sequence.CurrentNumber,
            PreviousNextNumber = previousNext,
            NewNextNumber = sequence.CurrentNumber + 1,
            Reason = reason,
            UserId = operationalContext.UserId,
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        return MapDocumentSequence(sequence, maxUsed);
    }

    public async Task<IReadOnlyList<DocumentSequenceAuditDto>> GetDocumentSequenceAuditsAsync(int id)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var sequenceExists = await _context.DocumentSequences
            .AnyAsync(s => s.Id == id && s.CompanyId == operationalContext.CompanyId);

        if (!sequenceExists)
        {
            throw new KeyNotFoundException("DOCUMENT_SEQUENCE_NOT_FOUND");
        }

        return await _context.DocumentSequenceAudits
            .AsNoTracking()
            .Where(a => a.DocumentSequenceId == id && a.CompanyId == operationalContext.CompanyId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new DocumentSequenceAuditDto
            {
                Id = a.Id,
                DocumentSequenceId = a.DocumentSequenceId,
                DocumentType = a.DocumentType,
                PreviousCurrentNumber = a.PreviousCurrentNumber,
                NewCurrentNumber = a.NewCurrentNumber,
                PreviousNextNumber = a.PreviousNextNumber,
                NewNextNumber = a.NewNextNumber,
                Reason = a.Reason,
                UserId = a.UserId,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    private async Task<Company> GetCompanyAsync(int companyId)
    {
        return await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId && c.IsActive)
            ?? throw new KeyNotFoundException("COMPANY_NOT_FOUND");
    }

    private async Task<CompanySriSettings> GetOrCreateCompanySriSettingsAsync(int companyId, int userId)
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
            CreatedAt = DateTime.UtcNow
        };

        _context.CompanySriSettings.Add(settings);
        await _context.SaveChangesAsync();

        return settings;
    }

    private async Task<CompanyBranding> GetOrCreateCompanyBrandingAsync(int companyId, int userId)
    {
        await GetCompanyAsync(companyId);

        var branding = await _context.CompanyBrandings
            .FirstOrDefaultAsync(b => b.CompanyId == companyId);

        if (branding is not null)
        {
            return branding;
        }

        var now = DateTime.UtcNow;
        branding = new CompanyBranding
        {
            CompanyId = companyId,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId
        };

        _context.CompanyBrandings.Add(branding);

        return branding;
    }

    private async Task ValidateOperationalStructureAsync(int companyId, int establishmentId, int emissionPointId)
    {
        var valid = await _context.Establishments
            .AsNoTracking()
            .AnyAsync(e =>
                e.Id == establishmentId
                && e.CompanyId == companyId
                && e.IsActive
                && e.EmissionPoints.Any(ep => ep.Id == emissionPointId && ep.IsActive));

        if (!valid)
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_SEQUENCE");
        }
    }

    private async Task<int> GetMaxUsedSequentialAsync(
        int companyId,
        int establishmentId,
        int emissionPointId,
        SaleDocumentType documentType)
    {
        return await _context.Sales
            .AsNoTracking()
            .Where(s =>
                s.CompanyId == companyId
                && s.EstablishmentId == establishmentId
                && s.EmissionPointId == emissionPointId
                && s.DocumentType == documentType
                && s.Sequential != null)
            .MaxAsync(s => (int?)s.Sequential) ?? 0;
    }

    private async Task<DocumentSequenceDto> GetDocumentSequenceDtoAsync(int id, int companyId)
    {
        var sequence = await _context.DocumentSequences
            .AsNoTracking()
            .Include(s => s.Establishment)
            .Include(s => s.EmissionPoint)
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId)
            ?? throw new KeyNotFoundException("DOCUMENT_SEQUENCE_NOT_FOUND");

        var maxUsed = await GetMaxUsedSequentialAsync(
            sequence.CompanyId,
            sequence.EstablishmentId,
            sequence.EmissionPointId,
            sequence.DocumentType);

        return MapDocumentSequence(sequence, maxUsed);
    }

    private static CompanyFiscalSettingsDto MapCompany(Company company)
    {
        return new CompanyFiscalSettingsDto
        {
            CompanyId = company.Id,
            Name = company.Name,
            TradeName = company.TradeName,
            Ruc = company.Ruc,
            MatrixAddress = company.MatrixAddress,
            Email = company.Email,
            Phone = company.Phone,
            IsAccountingRequired = company.IsAccountingRequired,
            SpecialTaxpayerNumber = company.SpecialTaxpayerNumber,
            TaxpayerRegime = company.TaxpayerRegime,
            IsActive = company.IsActive
        };
    }

    private static CompanySriSettingsDto MapSriSettings(CompanySriSettings settings)
    {
        return new CompanySriSettingsDto
        {
            CompanyId = settings.CompanyId,
            Environment = settings.Environment,
            EmissionType = settings.EmissionType,
            IsEnabled = settings.IsEnabled,
            CertificateConfigured = settings.CertificateConfigured,
            CertificateExpiresAt = settings.CertificateExpiresAt,
            UpdatedAt = settings.UpdatedAt
        };
    }

    private static CompanyBrandingDto MapBranding(int companyId, CompanyBranding? branding)
    {
        var logoConfigured = branding?.LogoBytes is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(branding.LogoContentType);

        return new CompanyBrandingDto
        {
            CompanyId = companyId,
            LogoConfigured = logoConfigured,
            LogoFileName = logoConfigured ? branding?.LogoFileName : null,
            LogoContentType = logoConfigured ? branding?.LogoContentType : null,
            LogoSizeBytes = logoConfigured ? branding?.LogoSizeBytes ?? branding?.LogoBytes?.LongLength : null,
            LogoUpdatedAt = logoConfigured ? branding?.LogoUpdatedAt : null,
            PrimaryColor = branding?.PrimaryColor,
            DocumentFooterText = branding?.DocumentFooterText
        };
    }

    private static DocumentSequenceDto MapDocumentSequence(DocumentSequence sequence, int maxUsed)
    {
        return new DocumentSequenceDto
        {
            Id = sequence.Id,
            CompanyId = sequence.CompanyId,
            EstablishmentId = sequence.EstablishmentId,
            EstablishmentCode = sequence.Establishment.Code,
            EstablishmentName = sequence.Establishment.Name,
            EmissionPointId = sequence.EmissionPointId,
            EmissionPointCode = sequence.EmissionPoint.Code,
            EmissionPointName = sequence.EmissionPoint.Name,
            DocumentType = sequence.DocumentType,
            CurrentNumber = sequence.CurrentNumber,
            NextNumber = sequence.CurrentNumber + 1,
            CreatedAt = sequence.CreatedAt,
            UpdatedAt = sequence.UpdatedAt,
            MaxUsedSequential = maxUsed
        };
    }

    private static string ValidateReason(string? value)
    {
        var reason = NormalizeOptional(value);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("DOCUMENT_SEQUENCE_REASON_REQUIRED");
        }

        if (reason.Length > 500)
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_SEQUENCE");
        }

        return reason;
    }

    private static void ValidateNextNumber(int nextNumber)
    {
        if (nextNumber < 1)
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_SEQUENCE");
        }
    }

    private static void ValidateDocumentType(SaleDocumentType documentType)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new InvalidOperationException("INVALID_DOCUMENT_SEQUENCE");
        }
    }

    private static void ValidateSriSettings(int environment, int emissionType)
    {
        if (environment is not (1 or 2))
        {
            throw new InvalidOperationException("INVALID_SRI_ENVIRONMENT");
        }

        if (emissionType != 1)
        {
            throw new InvalidOperationException("INVALID_SRI_EMISSION_TYPE");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeLogoContentType(string? value)
    {
        var normalized = NormalizeOptional(value)?.Split(';', 2)[0].Trim().ToLowerInvariant();

        return normalized switch
        {
            "image/jpg" or "image/pjpeg" => "image/jpeg",
            _ when normalized is not null && AllowedLogoContentTypes.Contains(normalized) => normalized,
            _ => null
        };
    }

    private static bool HasValidLogoSignature(byte[] bytes, string contentType)
        => contentType switch
        {
            "image/png" => bytes.Length >= 8
                && bytes[0] == 0x89
                && bytes[1] == 0x50
                && bytes[2] == 0x4E
                && bytes[3] == 0x47
                && bytes[4] == 0x0D
                && bytes[5] == 0x0A
                && bytes[6] == 0x1A
                && bytes[7] == 0x0A,
            "image/jpeg" => bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF,
            "image/webp" => bytes.Length >= 12
                && bytes[0] == 0x52
                && bytes[1] == 0x49
                && bytes[2] == 0x46
                && bytes[3] == 0x46
                && bytes[8] == 0x57
                && bytes[9] == 0x45
                && bytes[10] == 0x42
                && bytes[11] == 0x50,
            _ => false
        };

    private static string TruncateFileName(string? value)
    {
        var fileName = Path.GetFileName(NormalizeOptional(value) ?? "company-logo");

        return fileName.Length <= 255
            ? fileName
            : fileName[..255];
    }

    private static bool IsValidBrandingColor(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return value.Length is 4 or 7
            && value[0] == '#'
            && value.Skip(1).All(IsHex);
    }

    private static bool IsHex(char value)
        => value is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';

    private static bool IsNumeric(string? value, int expectedLength)
    {
        return value is not null
            && value.Length == expectedLength
            && value.All(char.IsDigit);
    }

    private static bool IsValidBasicEmail(string? value)
    {
        return value is null
            || (value.Contains('@') && value.Contains('.') && !value.Contains(' '));
    }
}
