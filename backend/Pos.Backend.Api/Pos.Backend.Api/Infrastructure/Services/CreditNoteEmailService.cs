using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class CreditNoteEmailService : ICreditNoteEmailService
{
    private const string DefaultTimeZoneId = "America/Guayaquil";
    private const string SuccessMessage =
        "Nota de crédito enviada por email correctamente.";
    private const string XmlContentType = "application/xml";
    private const string PdfContentType = "application/pdf";
    private const string DeliveryStatusSucceeded = "Succeeded";
    private const string DeliveryStatusFailed = "Failed";
    private const string DeliverySendFailedCode =
        "CREDIT_NOTE_EMAIL_SEND_FAILED";

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriCreditNoteSubmissionService
        _sriCreditNoteSubmissionService;
    private readonly ISriRidePdfService _sriRidePdfService;
    private readonly ICompanyEmailSettingsService _companyEmailSettingsService;
    private readonly IEmailSenderService _emailSenderService;
    private readonly IBusinessClockService _businessClockService;
    private readonly ILogger<CreditNoteEmailService> _logger;

    public CreditNoteEmailService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriCreditNoteSubmissionService sriCreditNoteSubmissionService,
        ISriRidePdfService sriRidePdfService,
        ICompanyEmailSettingsService companyEmailSettingsService,
        IEmailSenderService emailSenderService,
        IBusinessClockService businessClockService,
        ILogger<CreditNoteEmailService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _sriCreditNoteSubmissionService = sriCreditNoteSubmissionService;
        _sriRidePdfService = sriRidePdfService;
        _companyEmailSettingsService = companyEmailSettingsService;
        _emailSenderService = emailSenderService;
        _businessClockService = businessClockService;
        _logger = logger;
    }

    public async Task<SendCreditNoteEmailResultDto>
        SendAuthorizedCreditNoteEmailAsync(
            int creditNoteId,
            SendCreditNoteEmailRequestDto dto)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var operationalContext =
            await _operationalContextAccessor.GetRequiredContextAsync();
        var creditNote = await LoadCreditNoteForSendAsync(
            creditNoteId,
            operationalContext);
        ValidateCreditNoteCanBeSentByEmail(creditNote);

        var toEmail = NormalizeAndValidateEmail(dto.ToEmail, required: true)!;
        var ccEmail = NormalizeAndValidateEmail(dto.CcEmail, required: false);
        var subject = NormalizeSubject(dto.Subject, creditNote);
        var customMessage = NormalizeMessage(dto.Message);
        var authorizedXml = await GetAuthorizedXmlAsync(creditNote.Id);
        var ridePdf = await GetRidePdfAsync(creditNote.Id);
        var senderSettings = await _companyEmailSettingsService
            .GetConfiguredSenderSettingsAsync();
        var emailMessage = BuildEmailMessage(
            creditNote,
            toEmail,
            ccEmail,
            subject,
            customMessage,
            authorizedXml,
            ridePdf);

        try
        {
            await _emailSenderService.SendAsync(
                senderSettings.Settings,
                senderSettings.SmtpPassword,
                emailMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Authorized credit note email failed. CreditNoteId {CreditNoteId}, CompanyId {CompanyId}, DocumentNumber {DocumentNumber}, ToEmail {ToEmail}, ErrorCode {ErrorCode}",
                creditNote.Id,
                creditNote.CompanyId,
                creditNote.Number,
                toEmail,
                DeliverySendFailedCode);

            await TryPersistDeliveryAsync(
                creditNote,
                operationalContext.UserId,
                toEmail,
                ccEmail,
                subject,
                DeliveryStatusFailed,
                sentAt: null,
                createdAt: _businessClockService.UtcNow,
                DeliverySendFailedCode,
                ex.InnerException?.Message ?? ex.Message);

            throw new InvalidOperationException(DeliverySendFailedCode, ex);
        }

        var sentAt = _businessClockService.UtcNow;

        await TryPersistDeliveryAsync(
            creditNote,
            operationalContext.UserId,
            toEmail,
            ccEmail,
            subject,
            DeliveryStatusSucceeded,
            sentAt,
            sentAt,
            errorCode: null,
            errorMessage: null);

        _logger.LogInformation(
            "Authorized credit note email sent. CreditNoteId {CreditNoteId}, CompanyId {CompanyId}, DocumentNumber {DocumentNumber}, ToEmail {ToEmail}",
            creditNote.Id,
            creditNote.CompanyId,
            creditNote.Number,
            toEmail);

        return new SendCreditNoteEmailResultDto
        {
            Success = true,
            Message = SuccessMessage,
            SentAt = sentAt,
            ToEmail = toEmail,
            CcEmail = ccEmail,
            DocumentNumber = creditNote.Number,
            AuthorizationNumber = creditNote.AuthorizationNumber
        };
    }

    public async Task<IReadOnlyList<SaleInvoiceEmailDeliveryDto>>
        GetDeliveriesAsync(int creditNoteId)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var operationalContext =
            await _operationalContextAccessor.GetRequiredContextAsync();

        var creditNoteExists = await _context.CreditNotes
            .AsNoTracking()
            .AnyAsync(note =>
                note.Id == creditNoteId
                && note.CompanyId == operationalContext.CompanyId
                && note.OriginalSale.CompanyId == operationalContext.CompanyId
                && note.OriginalSale.EstablishmentId
                    == operationalContext.EstablishmentId
                && note.OriginalSale.EmissionPointId
                    == operationalContext.EmissionPointId);

        if (!creditNoteExists)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        return await _context.SaleInvoiceEmailDeliveries
            .AsNoTracking()
            .Where(delivery =>
                delivery.CreditNoteId == creditNoteId
                && delivery.CompanyId == operationalContext.CompanyId)
            .OrderByDescending(delivery => delivery.CreatedAt)
            .ThenByDescending(delivery => delivery.Id)
            .Select(delivery => new SaleInvoiceEmailDeliveryDto
            {
                Id = delivery.Id,
                SaleId = null,
                CreditNoteId = delivery.CreditNoteId,
                ToEmail = delivery.ToEmail,
                CcEmail = delivery.CcEmail,
                Subject = delivery.Subject,
                Status = delivery.Status,
                SentAt = delivery.SentAt,
                CreatedAt = delivery.CreatedAt,
                CreatedByUserId = delivery.CreatedByUserId,
                DocumentNumberSnapshot = delivery.DocumentNumberSnapshot,
                AuthorizationNumberSnapshot =
                    delivery.AuthorizationNumberSnapshot,
                ErrorCode = delivery.ErrorCode,
                ErrorMessage = delivery.ErrorMessage
            })
            .ToListAsync();
    }

    private async Task<CreditNote> LoadCreditNoteForSendAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var creditNote = await _context.CreditNotes
            .AsNoTracking()
            .Include(note => note.Company)
            .SingleOrDefaultAsync(note =>
                note.Id == creditNoteId
                && note.CompanyId == operationalContext.CompanyId
                && note.EstablishmentId == operationalContext.EstablishmentId
                && note.EmissionPointId == operationalContext.EmissionPointId
                && note.OriginalSale.CompanyId == operationalContext.CompanyId
                && note.OriginalSale.EstablishmentId
                    == operationalContext.EstablishmentId
                && note.OriginalSale.EmissionPointId
                    == operationalContext.EmissionPointId);

        return creditNote
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private static void ValidateCreditNoteCanBeSentByEmail(
        CreditNote creditNote)
    {
        if (creditNote.VoidedAt.HasValue
            || creditNote.DocumentStatus == SaleDocumentStatus.Cancelled)
        {
            throw new InvalidOperationException("CREDIT_NOTE_EMAIL_VOIDED");
        }

        var isAuthorized =
            creditNote.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(
                creditNote.SriAuthorizationStatus,
                "AUTORIZADO",
                StringComparison.OrdinalIgnoreCase);

        if (!isAuthorized
            || string.IsNullOrWhiteSpace(creditNote.AuthorizationNumber)
            || string.IsNullOrWhiteSpace(creditNote.AccessKey))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_NOT_AUTHORIZED");
        }
    }

    private async Task<string> GetAuthorizedXmlAsync(int creditNoteId)
    {
        try
        {
            var authorizedXml = await _sriCreditNoteSubmissionService
                .GetAuthorizedXmlAsync(creditNoteId);

            if (string.IsNullOrWhiteSpace(authorizedXml))
            {
                throw new InvalidOperationException(
                    "CREDIT_NOTE_EMAIL_AUTHORIZED_XML_NOT_AVAILABLE");
            }

            return authorizedXml;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Authorized XML is not available for credit note email. CreditNoteId {CreditNoteId}, ErrorCode {ErrorCode}",
                creditNoteId,
                "CREDIT_NOTE_EMAIL_AUTHORIZED_XML_NOT_AVAILABLE");
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_AUTHORIZED_XML_NOT_AVAILABLE",
                ex);
        }
    }

    private async Task<SriRidePdfFileResult> GetRidePdfAsync(int creditNoteId)
    {
        try
        {
            var ridePdf = await _sriRidePdfService
                .GenerateCreditNoteAsync(creditNoteId);

            if (ridePdf.Bytes.Length == 0
                || string.IsNullOrWhiteSpace(ridePdf.FileName)
                || !string.Equals(
                    ridePdf.ContentType?.Trim(),
                    PdfContentType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "CREDIT_NOTE_EMAIL_RIDE_PDF_NOT_AVAILABLE");
            }

            return ridePdf;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "RIDE PDF is not available for credit note email. CreditNoteId {CreditNoteId}, ErrorCode {ErrorCode}",
                creditNoteId,
                "CREDIT_NOTE_EMAIL_RIDE_PDF_NOT_AVAILABLE");
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_RIDE_PDF_NOT_AVAILABLE",
                ex);
        }
    }

    private async Task TryPersistDeliveryAsync(
        CreditNote creditNote,
        int userId,
        string toEmail,
        string? ccEmail,
        string subject,
        string status,
        DateTime? sentAt,
        DateTime createdAt,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            _context.SaleInvoiceEmailDeliveries.Add(
                new SaleInvoiceEmailDelivery
                {
                    SaleId = null,
                    CreditNoteId = creditNote.Id,
                    CompanyId = creditNote.CompanyId,
                    EstablishmentId = creditNote.EstablishmentId,
                    EmissionPointId = creditNote.EmissionPointId,
                    ToEmail = toEmail,
                    CcEmail = ccEmail,
                    Subject = subject,
                    Status = status,
                    SentAt = sentAt,
                    CreatedAt = createdAt,
                    CreatedByUserId = userId,
                    DocumentNumberSnapshot = creditNote.Number,
                    AuthorizationNumberSnapshot =
                        creditNote.AuthorizationNumber,
                    ErrorCode = errorCode,
                    ErrorMessage = Truncate(errorMessage, 500)
                });

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not persist credit note email delivery audit. CreditNoteId {CreditNoteId}, CompanyId {CompanyId}, Status {Status}",
                creditNote.Id,
                creditNote.CompanyId,
                status);
        }
    }

    private OutboundEmailMessage BuildEmailMessage(
        CreditNote creditNote,
        string toEmail,
        string? ccEmail,
        string subject,
        string? customMessage,
        string authorizedXml,
        SriRidePdfFileResult ridePdf)
    {
        var documentNumber = ValueOrDash(creditNote.Number);
        var originalDocumentNumber = ValueOrDash(
            creditNote.OriginalSaleNumberSnapshot);
        var authorizationNumber = ValueOrDash(
            creditNote.AuthorizationNumber);
        var authorizationDate = FormatAuthorizationDate(
            creditNote.AuthorizedAt,
            creditNote.TimeZoneIdSnapshot);
        var reason = ValueOrDash(creditNote.Reason);
        var total = FormatMoney(creditNote.Total);
        var companyName = CompanyName(creditNote.Company);
        var safeCustomMessage = FormatHtmlParagraphs(customMessage);

        var htmlBody = $"""
            <p>Estimado cliente,</p>
            {safeCustomMessage}
            <p>Adjuntamos la nota de crédito electrónica autorizada por el SRI.</p>
            <table style="border-collapse: collapse; margin: 12px 0;">
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Nota de crédito</strong></td><td>{HtmlEncode(documentNumber)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Factura modificada</strong></td><td>{HtmlEncode(originalDocumentNumber)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Autorización</strong></td><td>{HtmlEncode(authorizationNumber)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Fecha de autorización</strong></td><td>{HtmlEncode(authorizationDate)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Motivo</strong></td><td>{HtmlEncode(reason)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Total</strong></td><td>{HtmlEncode(total)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Emisor</strong></td><td>{HtmlEncode(companyName)}</td></tr>
            </table>
            <p>Se adjuntan el XML autorizado y el RIDE PDF.</p>
            <p>Este correo fue generado automáticamente por HFPOS.</p>
            """;

        var textBody = $"""
            Estimado cliente,

            {TextParagraph(customMessage)}Adjuntamos la nota de crédito electrónica autorizada por el SRI.

            Nota de crédito: {documentNumber}
            Factura modificada: {originalDocumentNumber}
            Autorización: {authorizationNumber}
            Fecha de autorización: {authorizationDate}
            Motivo: {reason}
            Total: {total}
            Emisor: {companyName}

            Se adjuntan el XML autorizado y el RIDE PDF.
            Este correo fue generado automáticamente por HFPOS.
            """;

        return new OutboundEmailMessage
        {
            To = toEmail,
            Cc = ccEmail,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Attachments =
            [
                new OutboundEmailAttachment
                {
                    FileName = BuildAuthorizedXmlFileName(creditNote),
                    ContentType = XmlContentType,
                    Bytes = Encoding.UTF8.GetBytes(authorizedXml)
                },
                new OutboundEmailAttachment
                {
                    FileName = ridePdf.FileName,
                    ContentType = ridePdf.ContentType,
                    Bytes = ridePdf.Bytes
                }
            ]
        };
    }

    private static string? NormalizeAndValidateEmail(
        string? value,
        bool required)
    {
        var normalized = NormalizeOptional(value);

        if (required && normalized is null)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_INVALID_ADDRESS");
        }

        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > 320 || !IsValidEmail(normalized))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_INVALID_ADDRESS");
        }

        return normalized;
    }

    private static string NormalizeSubject(
        string? value,
        CreditNote creditNote)
    {
        var subject = NormalizeOptional(value)
            ?? $"Nota de credito electronica {ValueOrDash(creditNote.Number)} - {CompanyName(creditNote.Company)}";

        if (subject.Length > 180)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_OPERATION_FAILED");
        }

        return subject;
    }

    private static string? NormalizeMessage(string? value)
    {
        var normalized = NormalizeOptional(value);

        if (normalized is not null && normalized.Length > 2000)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_EMAIL_OPERATION_FAILED");
        }

        return normalized;
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(
                address.Address,
                value,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildAuthorizedXmlFileName(CreditNote creditNote)
    {
        var identifier = NormalizeOptional(creditNote.Number)
            ?? creditNote.Id.ToString(CultureInfo.InvariantCulture);
        return $"nota-credito-{SanitizeFileNamePart(identifier)}-autorizado.xml";
    }

    private static string SanitizeFileNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character == '-'
                    ? character
                    : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized)
            ? "nota-credito"
            : sanitized;
    }

    private string FormatAuthorizationDate(
        DateTime? value,
        string? timeZoneId)
    {
        if (!value.HasValue)
        {
            return "-";
        }

        var utcInstant = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => new DateTimeOffset(value.Value, TimeSpan.Zero).UtcDateTime
        };

        TimeZoneInfo timeZone;
        try
        {
            timeZone = _businessClockService.ResolveTimeZone(
                NormalizeOptional(timeZoneId) ?? DefaultTimeZoneId);
        }
        catch (InvalidOperationException)
        {
            timeZone = _businessClockService.ResolveTimeZone(
                DefaultTimeZoneId);
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utcInstant, timeZone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatHtmlParagraphs(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var encoded = HtmlEncode(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $"<p>{encoded}</p>";
    }

    private static string TextParagraph(string? value)
        => value is null ? string.Empty : $"{value}\n\n";

    private static string FormatMoney(decimal value)
        => "$" + value.ToString(
            "#,##0.00",
            CultureInfo.GetCultureInfo("en-US"));

    private static string CompanyName(Company company)
        => NormalizeOptional(company.TradeName) ?? company.Name;

    private static string ValueOrDash(string? value)
        => NormalizeOptional(value) ?? "-";

    private static string HtmlEncode(string value)
        => HtmlEncoder.Default.Encode(value);

    private static string? Truncate(string? value, int maxLength)
    {
        var normalized = NormalizeOptional(value);

        return normalized is null || normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
