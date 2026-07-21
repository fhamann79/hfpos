using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriCreditNoteSigningService : ISriCreditNoteSigningService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriSigningCertificateProvider _certificateProvider;
    private readonly ISriXadesBesSigner _sriXadesBesSigner;
    private readonly ISriCreditNoteXmlValidator _sriCreditNoteXmlValidator;
    private readonly ICreditNoteService _creditNoteService;
    private readonly IBusinessClockService _businessClockService;
    private readonly ILogger<SriCreditNoteSigningService> _logger;

    public SriCreditNoteSigningService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriSigningCertificateProvider certificateProvider,
        ISriXadesBesSigner sriXadesBesSigner,
        ISriCreditNoteXmlValidator sriCreditNoteXmlValidator,
        ICreditNoteService creditNoteService,
        IBusinessClockService businessClockService,
        ILogger<SriCreditNoteSigningService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _certificateProvider = certificateProvider;
        _sriXadesBesSigner = sriXadesBesSigner;
        _sriCreditNoteXmlValidator = sriCreditNoteXmlValidator;
        _creditNoteService = creditNoteService;
        _businessClockService = businessClockService;
        _logger = logger;
    }

    public async Task<CreditNoteDto> SignDraftAsync(int creditNoteId)
    {
        OperationalContext? operationalContext = null;

        try
        {
            if (creditNoteId <= 0)
            {
                throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
            }

            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var originalSaleId = await GetOriginalSaleIdAsync(
                creditNoteId,
                operationalContext);

            await LockOriginalSaleAsync(originalSaleId, operationalContext);
            var creditNote = await LockCreditNoteAsync(
                creditNoteId,
                operationalContext);

            if (HasCompleteSignature(creditNote))
            {
                await transaction.CommitAsync();
                return await GetCurrentCreditNoteAsync(creditNoteId);
            }

            if (HasAnySignatureData(creditNote))
            {
                throw new InvalidOperationException(
                    "CREDIT_NOTE_SRI_SIGNATURE_INCONSISTENT");
            }

            ValidateCanSign(creditNote);
            _sriCreditNoteXmlValidator.ValidateUnsignedCreditNoteXml(
                creditNote.SriXmlDraft!);
            ValidateXmlAccessKey(
                creditNote.SriXmlDraft!,
                creditNote.AccessKey!);

            using var certificateMaterial =
                await _certificateProvider.GetActiveCertificateMaterialAsync();
            var now = _businessClockService.UtcNow;
            var signedXml = _sriXadesBesSigner.SignCreditNoteXml(
                creditNote.SriXmlDraft!,
                certificateMaterial.Certificate,
                creditNote.AccessKey!,
                now);

            creditNote.SriSignedXml = signedXml;
            creditNote.SriSignedAt = now;
            creditNote.SriSignatureHash = ComputeSha256Hash(signedXml);
            creditNote.SriSigningCertificateThumbprint =
                certificateMaterial.Thumbprint;
            creditNote.SriSigningCertificateSubject =
                certificateMaterial.Subject;
            creditNote.SriSigningCertificateSerialNumber =
                certificateMaterial.SerialNumber;
            creditNote.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "SRI credit note XML draft signed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} CertificateId {CertificateId}",
                creditNote.Id,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                certificateMaterial.CertificateId);

            return await GetCurrentCreditNoteAsync(creditNoteId);
        }
        catch (Exception ex) when (IsDomainError(ex))
        {
            _logger.LogWarning(
                ex,
                "SRI credit note XML signing failed. CreditNoteId {CreditNoteId} ErrorCode {ErrorCode} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                creditNoteId,
                ex.Message,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected SRI credit note XML signing failure. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                creditNoteId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED", ex);
        }
    }

    public async Task<string> GetSignedXmlAsync(int creditNoteId)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var operationalContext =
            await _operationalContextAccessor.GetRequiredContextAsync();

        try
        {
            var result = await _context.CreditNotes
                .AsNoTracking()
                .Where(creditNote =>
                    creditNote.Id == creditNoteId
                    && creditNote.CompanyId == operationalContext.CompanyId
                    && creditNote.OriginalSale.CompanyId
                        == operationalContext.CompanyId
                    && creditNote.OriginalSale.EstablishmentId
                        == operationalContext.EstablishmentId
                    && creditNote.OriginalSale.EmissionPointId
                        == operationalContext.EmissionPointId)
                .Select(creditNote => new { creditNote.SriSignedXml })
                .SingleOrDefaultAsync();

            if (result is null)
            {
                throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
            }

            if (string.IsNullOrWhiteSpace(result.SriSignedXml))
            {
                throw new KeyNotFoundException(
                    "CREDIT_NOTE_SRI_SIGNED_XML_NOT_FOUND");
            }

            return result.SriSignedXml;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SRI credit note signed XML query failed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                creditNoteId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId);
            throw new InvalidOperationException("CREDIT_NOTE_OPERATION_FAILED", ex);
        }
    }

    private async Task<int> GetOriginalSaleIdAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var originalSaleId = await _context.CreditNotes
            .AsNoTracking()
            .Where(creditNote =>
                creditNote.Id == creditNoteId
                && creditNote.CompanyId == operationalContext.CompanyId
                && creditNote.EstablishmentId
                    == operationalContext.EstablishmentId
                && creditNote.EmissionPointId
                    == operationalContext.EmissionPointId)
            .Select(creditNote => (int?)creditNote.OriginalSaleId)
            .SingleOrDefaultAsync();

        return originalSaleId
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private async Task LockOriginalSaleAsync(
        int originalSaleId,
        OperationalContext operationalContext)
    {
        var originalSale = await _context.Sales
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Sales""
                WHERE ""Id"" = {originalSaleId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        if (originalSale is null)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }
    }

    private async Task<CreditNote> LockCreditNoteAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var creditNote = await _context.CreditNotes
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""CreditNotes""
                WHERE ""Id"" = {creditNoteId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        return creditNote
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private async Task<CreditNoteDto> GetCurrentCreditNoteAsync(int creditNoteId)
    {
        return await _creditNoteService.GetByIdAsync(creditNoteId)
            ?? throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
    }

    private static void ValidateCanSign(CreditNote creditNote)
    {
        if (creditNote.DocumentStatus == SaleDocumentStatus.Cancelled
            || creditNote.VoidedAt.HasValue)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SIGN_CANCELLED");
        }

        if (creditNote.DocumentStatus != SaleDocumentStatus.Draft)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SIGN_NOT_ALLOWED");
        }

        if (string.IsNullOrWhiteSpace(creditNote.AccessKey))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_ACCESS_KEY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(creditNote.SriXmlDraft))
        {
            throw new KeyNotFoundException(
                "CREDIT_NOTE_SRI_XML_DRAFT_NOT_FOUND");
        }

        if (!creditNote.SriXmlGeneratedAt.HasValue)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_DRAFT_INCONSISTENT");
        }

        if (HasDownstreamProcessStarted(creditNote))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_PROCESS_ALREADY_STARTED");
        }
    }

    private static void ValidateXmlAccessKey(
        string unsignedXml,
        string expectedAccessKey)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var stringReader = new StringReader(unsignedXml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        var document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        var xmlAccessKey = root?
            .Elements()
            .SingleOrDefault(element => element.Name.LocalName == "infoTributaria")?
            .Elements()
            .SingleOrDefault(element => element.Name.LocalName == "claveAcceso")?
            .Value;

        if (root?.Name.LocalName != "notaCredito"
            || root.Attribute("id")?.Value != "comprobante"
            || !string.Equals(
                xmlAccessKey,
                expectedAccessKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_XML_ACCESS_KEY_MISMATCH");
        }
    }

    private static bool HasCompleteSignature(CreditNote creditNote)
    {
        return !string.IsNullOrWhiteSpace(creditNote.SriSignedXml)
            && creditNote.SriSignedAt.HasValue
            && !string.IsNullOrWhiteSpace(creditNote.SriSignatureHash)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateThumbprint)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateSubject)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateSerialNumber);
    }

    private static bool HasAnySignatureData(CreditNote creditNote)
    {
        return creditNote.SriSignedXml is not null
            || creditNote.SriSignedAt.HasValue
            || creditNote.SriSignatureHash is not null
            || creditNote.SriSigningCertificateThumbprint is not null
            || creditNote.SriSigningCertificateSubject is not null
            || creditNote.SriSigningCertificateSerialNumber is not null;
    }

    private static bool HasDownstreamProcessStarted(CreditNote creditNote)
    {
        return creditNote.SriSubmittedAt.HasValue
            || !string.IsNullOrWhiteSpace(creditNote.SriReceptionStatus)
            || !string.IsNullOrWhiteSpace(creditNote.SriAuthorizationStatus)
            || !string.IsNullOrWhiteSpace(creditNote.AuthorizationNumber)
            || creditNote.AuthorizedAt.HasValue
            || creditNote.SriLastCheckedAt.HasValue;
    }

    private static string ComputeSha256Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsDomainError(Exception exception)
    {
        return exception switch
        {
            KeyNotFoundException => true,
            InvalidOperationException invalidOperationException =>
                DomainErrorCodes.Contains(invalidOperationException.Message),
            _ => false
        };
    }

    private static readonly HashSet<string> DomainErrorCodes =
        new(StringComparer.Ordinal)
        {
            "CREDIT_NOTE_NOT_FOUND",
            "CREDIT_NOTE_SRI_ACCESS_KEY_REQUIRED",
            "CREDIT_NOTE_SRI_DRAFT_INCONSISTENT",
            "CREDIT_NOTE_SRI_PROCESS_ALREADY_STARTED",
            "CREDIT_NOTE_SRI_SIGNATURE_INCONSISTENT",
            "CREDIT_NOTE_SRI_SIGN_CANCELLED",
            "CREDIT_NOTE_SRI_SIGN_NOT_ALLOWED",
            "CREDIT_NOTE_SRI_XML_ACCESS_KEY_MISMATCH",
            "SRI_CREDIT_NOTE_XML_SCHEMA_VALIDATION_FAILED",
            "CERTIFICATE_EXPIRED",
            "CERTIFICATE_WITHOUT_PRIVATE_KEY",
            "CERTIFICATE_LOAD_FAILED",
            "CERTIFICATE_UNPROTECT_FAILED",
            "SRI_SIGNATURE_VALIDATION_FAILED",
            "SRI_XML_SIGNING_FAILED"
        };
}
