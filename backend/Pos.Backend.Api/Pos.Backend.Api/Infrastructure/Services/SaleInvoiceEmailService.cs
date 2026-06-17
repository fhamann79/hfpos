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

public class SaleInvoiceEmailService : ISaleInvoiceEmailService
{
    private const string SuccessMessage = "Factura enviada por email correctamente.";
    private const string XmlContentType = "application/xml";
    private const string DeliveryStatusSucceeded = "Succeeded";
    private const string DeliveryStatusFailed = "Failed";
    private const string DeliverySendFailedCode = "SALE_INVOICE_EMAIL_SEND_FAILED";

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriSubmissionService _sriSubmissionService;
    private readonly ISriRidePdfService _sriRidePdfService;
    private readonly ICompanyEmailSettingsService _companyEmailSettingsService;
    private readonly IEmailSenderService _emailSenderService;
    private readonly ILogger<SaleInvoiceEmailService> _logger;

    public SaleInvoiceEmailService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriSubmissionService sriSubmissionService,
        ISriRidePdfService sriRidePdfService,
        ICompanyEmailSettingsService companyEmailSettingsService,
        IEmailSenderService emailSenderService,
        ILogger<SaleInvoiceEmailService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _sriSubmissionService = sriSubmissionService;
        _sriRidePdfService = sriRidePdfService;
        _companyEmailSettingsService = companyEmailSettingsService;
        _emailSenderService = emailSenderService;
        _logger = logger;
    }

    public async Task<SendSaleInvoiceEmailResultDto> SendAuthorizedInvoiceEmailAsync(
        int saleId,
        SendSaleInvoiceEmailRequestDto dto)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var sale = await LoadSaleAsync(saleId, operationalContext);
        ValidateSaleCanBeSentByEmail(sale);

        var toEmail = NormalizeAndValidateEmail(dto.ToEmail, required: true);
        var ccEmail = NormalizeAndValidateEmail(dto.CcEmail, required: false);
        var subject = NormalizeSubject(dto.Subject, sale);
        var customMessage = NormalizeMessage(dto.Message);
        var authorizedXml = await GetAuthorizedXmlAsync(sale.Id);
        var ridePdf = await GetRidePdfAsync(sale.Id);
        var senderSettings = await _companyEmailSettingsService.GetConfiguredSenderSettingsAsync();

        var emailMessage = BuildEmailMessage(
            sale,
            toEmail!,
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
                ex,
                "Could not send authorized invoice email. SaleId {SaleId}, CompanyId {CompanyId}, ToEmail {ToEmail}, DocumentNumber {DocumentNumber}",
                sale.Id,
                sale.CompanyId,
                toEmail,
                sale.Number);

            await TryPersistDeliveryAsync(
                sale,
                operationalContext.UserId,
                toEmail!,
                ccEmail,
                subject,
                DeliveryStatusFailed,
                sentAt: null,
                DeliverySendFailedCode,
                ex.InnerException?.Message ?? ex.Message);

            throw new InvalidOperationException(DeliverySendFailedCode, ex);
        }

        var sentAt = DateTime.UtcNow;

        await TryPersistDeliveryAsync(
            sale,
            operationalContext.UserId,
            toEmail!,
            ccEmail,
            subject,
            DeliveryStatusSucceeded,
            sentAt,
            errorCode: null,
            errorMessage: null);

        _logger.LogInformation(
            "Authorized invoice email sent. SaleId {SaleId}, CompanyId {CompanyId}, ToEmail {ToEmail}, DocumentNumber {DocumentNumber}",
            sale.Id,
            sale.CompanyId,
            toEmail,
            sale.Number);

        return new SendSaleInvoiceEmailResultDto
        {
            Success = true,
            Message = SuccessMessage,
            SentAt = sentAt,
            ToEmail = toEmail!,
            CcEmail = ccEmail,
            DocumentNumber = sale.Number,
            AuthorizationNumber = sale.AuthorizationNumber
        };
    }

    public async Task<IReadOnlyList<SaleInvoiceEmailDeliveryDto>> GetDeliveriesAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var saleExists = await _context.Sales
            .AsNoTracking()
            .AnyAsync(s => s.Id == saleId
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        if (!saleExists)
        {
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        }

        return await _context.SaleInvoiceEmailDeliveries
            .AsNoTracking()
            .Where(d => d.SaleId == saleId
                && d.CompanyId == operationalContext.CompanyId
                && d.EstablishmentId == operationalContext.EstablishmentId
                && d.EmissionPointId == operationalContext.EmissionPointId)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new SaleInvoiceEmailDeliveryDto
            {
                Id = d.Id,
                SaleId = d.SaleId,
                ToEmail = d.ToEmail,
                CcEmail = d.CcEmail,
                Subject = d.Subject,
                Status = d.Status,
                SentAt = d.SentAt,
                CreatedAt = d.CreatedAt,
                CreatedByUserId = d.CreatedByUserId,
                DocumentNumberSnapshot = d.DocumentNumberSnapshot,
                AuthorizationNumberSnapshot = d.AuthorizationNumberSnapshot,
                ErrorCode = d.ErrorCode,
                ErrorMessage = d.ErrorMessage
            })
            .ToListAsync();
    }

    private async Task<Sale> LoadSaleAsync(int saleId, OperationalContext operationalContext)
    {
        var sale = await _context.Sales
            .AsNoTracking()
            .Include(s => s.Company)
            .FirstOrDefaultAsync(s => s.Id == saleId
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        return sale ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    private static void ValidateSaleCanBeSentByEmail(Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SALE_NOT_INVOICE");
        }

        if (sale.Status == SaleStatus.Voided)
        {
            throw new InvalidOperationException("SALE_VOIDED");
        }

        var isAuthorized = sale.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(sale.SriAuthorizationStatus, "AUTORIZADO", StringComparison.OrdinalIgnoreCase);

        if (!isAuthorized)
        {
            throw new InvalidOperationException("SALE_NOT_AUTHORIZED");
        }

        if (string.IsNullOrWhiteSpace(sale.AuthorizationNumber))
        {
            throw new InvalidOperationException("SALE_NOT_AUTHORIZED");
        }
    }

    private async Task<string> GetAuthorizedXmlAsync(int saleId)
    {
        try
        {
            var authorizedXml = await _sriSubmissionService.GetAuthorizedXmlAsync(saleId);

            if (string.IsNullOrWhiteSpace(authorizedXml))
            {
                throw new InvalidOperationException("SRI_AUTHORIZED_XML_NOT_AVAILABLE");
            }

            return authorizedXml;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Authorized XML is not available for invoice email. SaleId {SaleId}", saleId);
            throw new InvalidOperationException("SRI_AUTHORIZED_XML_NOT_AVAILABLE", ex);
        }
    }

    private async Task<SriRidePdfFileResult> GetRidePdfAsync(int saleId)
    {
        try
        {
            var ridePdf = await _sriRidePdfService.GenerateAsync(saleId);

            if (ridePdf.Bytes.Length == 0 || string.IsNullOrWhiteSpace(ridePdf.FileName))
            {
                throw new InvalidOperationException("SRI_RIDE_PDF_NOT_AVAILABLE");
            }

            return ridePdf;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "RIDE PDF is not available for invoice email. SaleId {SaleId}", saleId);
            throw new InvalidOperationException("SRI_RIDE_PDF_NOT_AVAILABLE", ex);
        }
    }

    private async Task TryPersistDeliveryAsync(
        Sale sale,
        int userId,
        string toEmail,
        string? ccEmail,
        string subject,
        string status,
        DateTime? sentAt,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            _context.SaleInvoiceEmailDeliveries.Add(new SaleInvoiceEmailDelivery
            {
                SaleId = sale.Id,
                CompanyId = sale.CompanyId,
                EstablishmentId = sale.EstablishmentId,
                EmissionPointId = sale.EmissionPointId,
                ToEmail = toEmail,
                CcEmail = ccEmail,
                Subject = subject,
                Status = status,
                SentAt = sentAt,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = userId,
                DocumentNumberSnapshot = sale.Number,
                AuthorizationNumberSnapshot = sale.AuthorizationNumber,
                ErrorCode = errorCode,
                ErrorMessage = Truncate(errorMessage, 500)
            });

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not persist sale invoice email delivery audit. SaleId {SaleId}, CompanyId {CompanyId}, Status {Status}",
                sale.Id,
                sale.CompanyId,
                status);
        }
    }

    private static OutboundEmailMessage BuildEmailMessage(
        Sale sale,
        string toEmail,
        string? ccEmail,
        string subject,
        string? customMessage,
        string authorizedXml,
        SriRidePdfFileResult ridePdf)
    {
        var documentNumber = ValueOrDash(sale.Number);
        var authorizationNumber = ValueOrDash(sale.AuthorizationNumber);
        var authorizationDate = FormatDateTime(sale.AuthorizedAt);
        var total = FormatMoney(sale.Total);
        var companyName = CompanyName(sale.Company);
        var safeCustomMessage = FormatHtmlParagraphs(customMessage);

        var htmlBody = $"""
            <p>Estimado cliente,</p>
            {safeCustomMessage}
            <p>Adjuntamos el comprobante electronico autorizado por el SRI.</p>
            <table style="border-collapse: collapse; margin: 12px 0;">
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Factura</strong></td><td>{HtmlEncode(documentNumber)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Autorizacion</strong></td><td>{HtmlEncode(authorizationNumber)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Fecha autorizacion</strong></td><td>{HtmlEncode(authorizationDate)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Total</strong></td><td>{HtmlEncode(total)}</td></tr>
              <tr><td style="padding: 4px 12px 4px 0;"><strong>Emisor</strong></td><td>{HtmlEncode(companyName)}</td></tr>
            </table>
            <p>Se adjunta el XML autorizado y el RIDE PDF.</p>
            <p>Este correo fue generado automaticamente por HFPOS.</p>
            """;

        var textBody = $"""
            Estimado cliente,

            {TextParagraph(customMessage)}
            Adjuntamos el comprobante electronico autorizado por el SRI.

            Factura: {documentNumber}
            Autorizacion: {authorizationNumber}
            Fecha autorizacion: {authorizationDate}
            Total: {total}
            Emisor: {companyName}

            Se adjunta el XML autorizado y el RIDE PDF.
            Este correo fue generado automaticamente por HFPOS.
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
                    FileName = BuildAuthorizedXmlFileName(sale),
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

    private static string? NormalizeAndValidateEmail(string? value, bool required)
    {
        var normalized = NormalizeOptional(value);

        if (required && normalized is null)
        {
            throw new InvalidOperationException("COMPANY_EMAIL_INVALID_ADDRESS");
        }

        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > 320 || !IsValidEmail(normalized))
        {
            throw new InvalidOperationException("COMPANY_EMAIL_INVALID_ADDRESS");
        }

        return normalized;
    }

    private static string NormalizeSubject(string? value, Sale sale)
    {
        var normalized = NormalizeOptional(value);

        if (normalized is not null && normalized.Length > 180)
        {
            throw new InvalidOperationException("SALE_INVOICE_EMAIL_OPERATION_FAILED");
        }

        return normalized
            ?? $"Factura electronica {ValueOrDash(sale.Number)} - {CompanyName(sale.Company)}";
    }

    private static string? NormalizeMessage(string? value)
    {
        var normalized = NormalizeOptional(value);

        if (normalized is not null && normalized.Length > 2000)
        {
            throw new InvalidOperationException("SALE_INVOICE_EMAIL_OPERATION_FAILED");
        }

        return normalized;
    }

    private static bool IsValidEmail(string value)
    {
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

    private static string BuildAuthorizedXmlFileName(Sale sale)
    {
        var identifier = NormalizeOptional(sale.Number) ?? $"sale-{sale.Id}";
        return $"{SanitizeFileNamePart(identifier)}-autorizado.xml";
    }

    private static string SanitizeFileNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '-' ? character : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "factura" : sanitized;
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

    private static string FormatDateTime(DateTime? value)
        => value?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatMoney(decimal value)
        => "$" + value.ToString("#,##0.00", CultureInfo.GetCultureInfo("en-US"));

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
