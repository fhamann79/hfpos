using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CompanyEmailSettingsService : ICompanyEmailSettingsService
{
    private const int DefaultSmtpPort = 587;
    private const string DefaultEncryptionMode = "StartTls";
    private const string TestSuccessMessage = "Correo de prueba enviado correctamente.";
    private const string TestFailureMessage = "No se pudo enviar el correo de prueba. Verifica host, puerto, seguridad, usuario, password/API key y remitente.";
    private static readonly HashSet<string> EncryptionModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "None",
        "StartTls",
        "SslOnConnect"
    };

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IEmailSenderService _emailSenderService;
    private readonly IDataProtector _protector;
    private readonly ILogger<CompanyEmailSettingsService> _logger;

    public CompanyEmailSettingsService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IEmailSenderService emailSenderService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CompanyEmailSettingsService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _emailSenderService = emailSenderService;
        _protector = dataProtectionProvider.CreateProtector(CompanyEmailProtectionPurposes.SmtpPasswordV1);
        _logger = logger;
    }

    public async Task<CompanyEmailSettingsDto> GetAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        await EnsureCompanyExistsAsync(operationalContext.CompanyId);

        var settings = await _context.CompanyEmailSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == operationalContext.CompanyId);

        return MapSettings(operationalContext.CompanyId, settings);
    }

    public async Task<CompanyEmailSettingsDto> UpdateAsync(UpdateCompanyEmailSettingsDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        await EnsureCompanyExistsAsync(operationalContext.CompanyId);

        var normalized = NormalizeAndValidateUpdate(dto);
        var settings = await _context.CompanyEmailSettings
            .FirstOrDefaultAsync(s => s.CompanyId == operationalContext.CompanyId);
        var now = DateTime.UtcNow;
        var previous = settings is null
            ? null
            : new EmailSenderConfigurationSnapshot(
                settings.SmtpHost,
                settings.SmtpPort,
                settings.EncryptionMode,
                settings.SmtpUsername,
                settings.FromEmail,
                !string.IsNullOrWhiteSpace(settings.SmtpPasswordProtected));

        if (settings is null)
        {
            settings = new CompanyEmailSettings
            {
                CompanyId = operationalContext.CompanyId,
                CreatedAt = now
            };

            _context.CompanyEmailSettings.Add(settings);
        }

        settings.IsEnabled = dto.IsEnabled;
        settings.SmtpHost = normalized.SmtpHost;
        settings.SmtpPort = dto.SmtpPort;
        settings.EncryptionMode = normalized.EncryptionMode;
        settings.SmtpUsername = normalized.SmtpUsername;
        settings.FromEmail = normalized.FromEmail;
        settings.FromDisplayName = normalized.FromDisplayName;
        settings.ReplyToEmail = normalized.ReplyToEmail;
        settings.UpdatedAt = now;
        settings.UpdatedByUserId = operationalContext.UserId;

        if (dto.ClearPassword)
        {
            settings.SmtpPasswordProtected = null;
        }

        var smtpPassword = NormalizeOptional(dto.SmtpPassword);
        var passwordChanged = dto.ClearPassword || smtpPassword is not null;
        if (smtpPassword is not null)
        {
            try
            {
                settings.SmtpPasswordProtected = _protector.Protect(smtpPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to protect SMTP password for company {CompanyId}", operationalContext.CompanyId);
                throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED", ex);
            }
        }

        if (HasSenderConfigurationChanged(previous, normalized, dto.SmtpPort, settings, passwordChanged))
        {
            settings.LastTestedAt = null;
            settings.LastTestSucceeded = null;
            settings.LastTestMessage = null;
        }

        await _context.SaveChangesAsync();

        return MapSettings(settings.CompanyId, settings);
    }

    public async Task<CompanyEmailTestResultDto> SendTestAsync(TestCompanyEmailSettingsDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var toEmail = NormalizeOptional(dto.ToEmail);

        if (!IsValidEmail(toEmail))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_INVALID_ADDRESS");
        }

        var settings = await _context.CompanyEmailSettings
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.CompanyId == operationalContext.CompanyId);

        if (settings is null)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_SETTINGS_NOT_CONFIGURED");
        }

        ValidateSettingsForTest(settings);

        var password = UnprotectPassword(settings);
        var testedAt = DateTime.UtcNow;
        var message = BuildTestMessage(settings.Company, toEmail!, testedAt);

        try
        {
            await _emailSenderService.SendAsync(settings, password, message);
            settings.LastTestedAt = testedAt;
            settings.LastTestSucceeded = true;
            settings.LastTestMessage = TestSuccessMessage;
            settings.UpdatedAt = testedAt;
            settings.UpdatedByUserId = operationalContext.UserId;

            await _context.SaveChangesAsync();

            return new CompanyEmailTestResultDto
            {
                Success = true,
                Message = TestSuccessMessage,
                TestedAt = testedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Company email test failed for company {CompanyId}. Host {SmtpHost}, port {SmtpPort}, encryption {EncryptionMode}",
                operationalContext.CompanyId,
                settings.SmtpHost,
                settings.SmtpPort,
                settings.EncryptionMode);

            settings.LastTestedAt = testedAt;
            settings.LastTestSucceeded = false;
            settings.LastTestMessage = TestFailureMessage;
            settings.UpdatedAt = testedAt;
            settings.UpdatedByUserId = operationalContext.UserId;

            await _context.SaveChangesAsync();

            return new CompanyEmailTestResultDto
            {
                Success = false,
                Message = TestFailureMessage,
                TestedAt = testedAt
            };
        }
    }

    public async Task<CompanyEmailSenderSettings> GetConfiguredSenderSettingsAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        await EnsureCompanyExistsAsync(operationalContext.CompanyId);

        var settings = await _context.CompanyEmailSettings
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.CompanyId == operationalContext.CompanyId);

        if (settings is null)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_SETTINGS_NOT_CONFIGURED");
        }

        ValidateSettingsForSending(settings);

        return new CompanyEmailSenderSettings
        {
            Settings = settings,
            SmtpPassword = UnprotectPassword(settings)
        };
    }

    private async Task EnsureCompanyExistsAsync(int companyId)
    {
        var exists = await _context.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.IsActive);

        if (!exists)
        {
            throw new KeyNotFoundException("COMPANY_NOT_FOUND");
        }
    }

    private static NormalizedEmailSettings NormalizeAndValidateUpdate(UpdateCompanyEmailSettingsDto dto)
    {
        var smtpHost = NormalizeOptional(dto.SmtpHost);
        var encryptionMode = NormalizeEncryptionMode(dto.EncryptionMode);
        var smtpUsername = NormalizeOptional(dto.SmtpUsername);
        var fromEmail = NormalizeOptional(dto.FromEmail);
        var fromDisplayName = NormalizeOptional(dto.FromDisplayName);
        var replyToEmail = NormalizeOptional(dto.ReplyToEmail);

        if (dto.SmtpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED");
        }

        if (dto.IsEnabled && smtpHost is null)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_SMTP_HOST_REQUIRED");
        }

        if (smtpHost?.Length > 255
            || smtpUsername?.Length > 255
            || fromDisplayName?.Length > 150
            || fromEmail?.Length > 320
            || replyToEmail?.Length > 320)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED");
        }

        if (dto.IsEnabled && fromEmail is null)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_FROM_REQUIRED");
        }

        if (!IsValidEmail(fromEmail) || !IsValidEmail(replyToEmail))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_INVALID_ADDRESS");
        }

        return new NormalizedEmailSettings(
            smtpHost,
            encryptionMode,
            smtpUsername,
            fromEmail,
            fromDisplayName,
            replyToEmail);
    }

    private static void ValidateSettingsForTest(CompanyEmailSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_SMTP_HOST_REQUIRED");
        }

        if (settings.SmtpPort is < 1 or > 65535 || !IsValidEncryptionMode(settings.EncryptionMode))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED");
        }

        if (string.IsNullOrWhiteSpace(settings.FromEmail))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_FROM_REQUIRED");
        }

        if (!IsValidEmail(settings.FromEmail) || !IsValidEmail(settings.ReplyToEmail))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_INVALID_ADDRESS");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpPasswordProtected))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_PASSWORD_REQUIRED");
        }
    }

    private static void ValidateSettingsForSending(CompanyEmailSettings settings)
    {
        if (!settings.IsEnabled)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_DISABLED");
        }

        ValidateSettingsForTest(settings);

        if (settings.LastTestSucceeded != true)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_NOT_TESTED");
        }
    }

    private static bool HasSenderConfigurationChanged(
        EmailSenderConfigurationSnapshot? previous,
        NormalizedEmailSettings normalized,
        int smtpPort,
        CompanyEmailSettings current,
        bool passwordChanged)
    {
        if (previous is null)
        {
            return true;
        }

        return !string.Equals(previous.SmtpHost, normalized.SmtpHost, StringComparison.Ordinal)
            || previous.SmtpPort != smtpPort
            || !string.Equals(previous.EncryptionMode, normalized.EncryptionMode, StringComparison.Ordinal)
            || !string.Equals(previous.SmtpUsername, normalized.SmtpUsername, StringComparison.Ordinal)
            || !string.Equals(previous.FromEmail, normalized.FromEmail, StringComparison.Ordinal)
            || previous.PasswordConfigured != (!string.IsNullOrWhiteSpace(current.SmtpPasswordProtected))
            || passwordChanged;
    }

    private string UnprotectPassword(CompanyEmailSettings settings)
    {
        try
        {
            return _protector.Unprotect(settings.SmtpPasswordProtected!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect SMTP password for company {CompanyId}", settings.CompanyId);
            throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED", ex);
        }
    }

    private static OutboundEmailMessage BuildTestMessage(Company company, string toEmail, DateTime testedAt)
    {
        var companyName = company.TradeName ?? company.Name;
        var safeCompanyName = HtmlEncoder.Default.Encode(companyName);
        var safeDate = HtmlEncoder.Default.Encode(testedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));

        return new OutboundEmailMessage
        {
            To = toEmail,
            Subject = "HFPOS - Prueba de configuracion de correo",
            HtmlBody = $"""
                <p>Hola,</p>
                <p>La configuracion SMTP de <strong>{safeCompanyName}</strong> funciona correctamente.</p>
                <p>Fecha de prueba: {safeDate}</p>
                <p>Este mensaje confirma que HFPOS puede enviar correos salientes con la configuracion guardada.</p>
                """,
            TextBody = $"""
                Hola,

                La configuracion SMTP de {companyName} funciona correctamente.
                Fecha de prueba: {testedAt:yyyy-MM-dd HH:mm:ss 'UTC'}.

                Este mensaje confirma que HFPOS puede enviar correos salientes con la configuracion guardada.
                """
        };
    }

    private static CompanyEmailSettingsDto MapSettings(int companyId, CompanyEmailSettings? settings)
    {
        return new CompanyEmailSettingsDto
        {
            CompanyId = companyId,
            IsEnabled = settings?.IsEnabled ?? false,
            SmtpHost = settings?.SmtpHost,
            SmtpPort = settings?.SmtpPort ?? DefaultSmtpPort,
            EncryptionMode = settings?.EncryptionMode ?? DefaultEncryptionMode,
            SmtpUsername = settings?.SmtpUsername,
            FromEmail = settings?.FromEmail,
            FromDisplayName = settings?.FromDisplayName,
            ReplyToEmail = settings?.ReplyToEmail,
            PasswordConfigured = !string.IsNullOrWhiteSpace(settings?.SmtpPasswordProtected),
            LastTestedAt = settings?.LastTestedAt,
            LastTestSucceeded = settings?.LastTestSucceeded,
            LastTestMessage = settings?.LastTestMessage
        };
    }

    private static string NormalizeEncryptionMode(string? value)
    {
        var normalized = NormalizeOptional(value) ?? DefaultEncryptionMode;

        if (!IsValidEncryptionMode(normalized))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_OPERATION_FAILED");
        }

        return EncryptionModes.First(mode => string.Equals(mode, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidEncryptionMode(string? value)
        => value is not null && EncryptionModes.Contains(value);

    private static bool IsValidEmail(string? value)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record NormalizedEmailSettings(
        string? SmtpHost,
        string EncryptionMode,
        string? SmtpUsername,
        string? FromEmail,
        string? FromDisplayName,
        string? ReplyToEmail);

    private sealed record EmailSenderConfigurationSnapshot(
        string? SmtpHost,
        int SmtpPort,
        string EncryptionMode,
        string? SmtpUsername,
        string? FromEmail,
        bool PasswordConfigured);
}
