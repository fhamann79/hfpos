using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriCreditNoteSubmissionService : ISriCreditNoteSubmissionService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriWebServiceClient _sriWebServiceClient;
    private readonly ICreditNoteService _creditNoteService;
    private readonly ISriCreditNoteXmlValidator _sriCreditNoteXmlValidator;
    private readonly IBusinessClockService _businessClockService;
    private readonly SriOptions _sriOptions;
    private readonly ILogger<SriCreditNoteSubmissionService> _logger;

    public SriCreditNoteSubmissionService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriWebServiceClient sriWebServiceClient,
        ICreditNoteService creditNoteService,
        ISriCreditNoteXmlValidator sriCreditNoteXmlValidator,
        IBusinessClockService businessClockService,
        IOptions<SriOptions> sriOptions,
        ILogger<SriCreditNoteSubmissionService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _sriWebServiceClient = sriWebServiceClient;
        _creditNoteService = creditNoteService;
        _sriCreditNoteXmlValidator = sriCreditNoteXmlValidator;
        _businessClockService = businessClockService;
        _sriOptions = sriOptions.Value;
        _logger = logger;
    }

    public async Task<CreditNoteDto> SubmitSignedAsync(int creditNoteId)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var operationalContext =
            await _operationalContextAccessor.GetRequiredContextAsync();
        var creditNote = await LoadCreditNoteSnapshotAsync(
            creditNoteId,
            operationalContext);

        if (!ValidateCanSubmit(creditNote))
        {
            return await GetCurrentCreditNoteAsync(creditNoteId);
        }

        ValidateUnsignedDraftIfPresent(creditNote);

        var sriContext = await ResolveSriSubmissionContextAsync(
            creditNote.CompanyId,
            creditNote.SriEnvironment);

        SriReceptionResponse response;

        try
        {
            response = await _sriWebServiceClient.SubmitAsync(
                creditNote.SriSignedXml!,
                sriContext.Environment);
        }
        catch (InvalidOperationException ex) when (
            IsReceptionExternalError(ex.Message))
        {
            await PersistFailedAttemptAsync(
                creditNote,
                operationalContext,
                sriContext.Environment,
                ex.Message,
                ex.InnerException?.Message ?? ex.Message);
            throw;
        }

        var responseMessage = response.Messages.FirstOrDefault();
        var now = _businessClockService.UtcNow;
        var wasAlreadyReceived = false;
        string? postCommitError = null;

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            var originalSaleId = await GetOriginalSaleIdAsync(
                creditNoteId,
                operationalContext);
            await LockOriginalSaleAsync(originalSaleId, operationalContext);
            var trackedCreditNote = await LockCreditNoteAsync(
                creditNoteId,
                operationalContext);

            if (!ValidateCanSubmit(trackedCreditNote))
            {
                wasAlreadyReceived = true;
                await transaction.CommitAsync();
            }
            else
            {
                ValidateUnsignedDraftIfPresent(trackedCreditNote);

                var attempt = BuildBaseAttempt(
                    trackedCreditNote,
                    operationalContext.UserId,
                    sriContext.Environment,
                    now);
                attempt.Status = response.IsReceived
                    ? SriSubmissionAttemptStatus.Success
                    : SriSubmissionAttemptStatus.Failed;
                attempt.ReceptionStatus = response.Estado;
                attempt.ResponseXml = response.RawResponseXml;
                ApplyMessage(attempt, responseMessage);

                trackedCreditNote.SriReceptionStatus = response.Estado;
                trackedCreditNote.SriLastSubmissionError = response.IsReceived
                    ? null
                    : response.ErrorSummary
                        ?? "Nota de cr\u00e9dito devuelta por el SRI.";
                trackedCreditNote.UpdatedAt = now;

                if (response.IsReceived)
                {
                    trackedCreditNote.DocumentStatus =
                        SaleDocumentStatus.PendingAuthorization;
                    trackedCreditNote.SriSubmittedAt = now;
                }
                else
                {
                    trackedCreditNote.DocumentStatus = SaleDocumentStatus.Rejected;
                    attempt.ErrorCode = "CREDIT_NOTE_SRI_RECEPTION_REJECTED";
                    attempt.ErrorMessage ??=
                        trackedCreditNote.SriLastSubmissionError;
                    postCommitError = "CREDIT_NOTE_SRI_RECEPTION_REJECTED";
                }

                _context.SriSubmissionAttempts.Add(attempt);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
        }

        if (postCommitError is not null)
        {
            throw new InvalidOperationException(postCommitError);
        }

        if (!wasAlreadyReceived)
        {
            _logger.LogInformation(
                "SRI credit note reception completed. CreditNoteId {CreditNoteId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} ReceptionStatus {ReceptionStatus}",
                creditNoteId,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                response.Estado);
        }

        return await GetCurrentCreditNoteAsync(creditNoteId);
    }

    public async Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(
        int creditNoteId)
    {
        if (creditNoteId <= 0)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        var operationalContext =
            await _operationalContextAccessor.GetRequiredContextAsync();

        var creditNoteExists = await _context.CreditNotes
            .AsNoTracking()
            .AnyAsync(creditNote =>
                creditNote.Id == creditNoteId
                && creditNote.CompanyId == operationalContext.CompanyId
                && creditNote.OriginalSale.CompanyId
                    == operationalContext.CompanyId
                && creditNote.OriginalSale.EstablishmentId
                    == operationalContext.EstablishmentId
                && creditNote.OriginalSale.EmissionPointId
                    == operationalContext.EmissionPointId);

        if (!creditNoteExists)
        {
            throw new KeyNotFoundException("CREDIT_NOTE_NOT_FOUND");
        }

        return await _context.SriSubmissionAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.CreditNoteId == creditNoteId
                && attempt.CompanyId == operationalContext.CompanyId)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new SriSubmissionAttemptDto
            {
                Id = attempt.Id,
                SaleId = attempt.SaleId,
                CreditNoteId = attempt.CreditNoteId,
                AccessKey = attempt.AccessKey,
                Environment = attempt.Environment,
                AttemptType = attempt.AttemptType,
                Status = attempt.Status,
                ReceptionStatus = attempt.ReceptionStatus,
                AuthorizationStatus = attempt.AuthorizationStatus,
                AuthorizationNumber = attempt.AuthorizationNumber,
                AuthorizationDate = attempt.AuthorizationDate,
                ErrorCode = attempt.ErrorCode,
                ErrorMessage = attempt.ErrorMessage,
                SriMessageIdentifier = attempt.SriMessageIdentifier,
                SriMessageType = attempt.SriMessageType,
                SriMessage = attempt.SriMessage,
                SriAdditionalInfo = attempt.SriAdditionalInfo,
                CreatedAt = attempt.CreatedAt,
                CreatedByUserId = attempt.CreatedByUserId
            })
            .ToListAsync();
    }

    private async Task<CreditNote> LoadCreditNoteSnapshotAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var creditNote = await _context.CreditNotes
            .AsNoTracking()
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

    private async Task<int> GetOriginalSaleIdAsync(
        int creditNoteId,
        OperationalContext operationalContext)
    {
        var originalSaleId = await _context.CreditNotes
            .AsNoTracking()
            .Where(note =>
                note.Id == creditNoteId
                && note.CompanyId == operationalContext.CompanyId
                && note.EstablishmentId == operationalContext.EstablishmentId
                && note.EmissionPointId == operationalContext.EmissionPointId
                && note.OriginalSale.CompanyId == operationalContext.CompanyId
                && note.OriginalSale.EstablishmentId
                    == operationalContext.EstablishmentId
                && note.OriginalSale.EmissionPointId
                    == operationalContext.EmissionPointId)
            .Select(note => (int?)note.OriginalSaleId)
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

    private static bool ValidateCanSubmit(CreditNote creditNote)
    {
        if (creditNote.DocumentStatus == SaleDocumentStatus.Cancelled
            || creditNote.VoidedAt.HasValue)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SUBMISSION_CANCELLED");
        }

        if (creditNote.DocumentStatus == SaleDocumentStatus.Authorized)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_ALREADY_AUTHORIZED");
        }

        if (creditNote.DocumentStatus == SaleDocumentStatus.Rejected)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_REJECTED_NOT_RESUBMITTABLE");
        }

        if (IsAlreadyReceived(creditNote))
        {
            return false;
        }

        if (creditNote.DocumentStatus != SaleDocumentStatus.Draft)
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SUBMISSION_NOT_ALLOWED");
        }

        if (string.IsNullOrWhiteSpace(creditNote.AccessKey))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_ACCESS_KEY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(creditNote.SriSignedXml))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SIGNED_XML_REQUIRED");
        }

        if (!HasCompleteSignature(creditNote))
        {
            throw new InvalidOperationException(
                "CREDIT_NOTE_SRI_SIGNATURE_INCONSISTENT");
        }

        return true;
    }

    private static bool IsAlreadyReceived(CreditNote creditNote)
        => creditNote.DocumentStatus == SaleDocumentStatus.PendingAuthorization
            || creditNote.SriSubmittedAt.HasValue
            || string.Equals(
                creditNote.SriReceptionStatus,
                "RECIBIDA",
                StringComparison.OrdinalIgnoreCase);

    private static bool HasCompleteSignature(CreditNote creditNote)
        => !string.IsNullOrWhiteSpace(creditNote.SriSignedXml)
            && creditNote.SriSignedAt.HasValue
            && !string.IsNullOrWhiteSpace(creditNote.SriSignatureHash)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateThumbprint)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateSubject)
            && !string.IsNullOrWhiteSpace(
                creditNote.SriSigningCertificateSerialNumber);

    private void ValidateUnsignedDraftIfPresent(CreditNote creditNote)
    {
        if (!string.IsNullOrWhiteSpace(creditNote.SriXmlDraft))
        {
            _sriCreditNoteXmlValidator.ValidateUnsignedCreditNoteXml(
                creditNote.SriXmlDraft);
        }
    }

    private async Task<(int Environment, int EmissionType)>
        ResolveSriSubmissionContextAsync(
            int companyId,
            int? creditNoteEnvironment)
    {
        var settings = await _context.CompanySriSettings
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new
            {
                item.Environment,
                item.EmissionType,
                item.IsEnabled
            })
            .FirstOrDefaultAsync();

        if (settings is not null && !settings.IsEnabled)
        {
            throw new InvalidOperationException("SRI_SETTINGS_DISABLED");
        }

        var environment = creditNoteEnvironment
            ?? settings?.Environment
            ?? _sriOptions.Environment;
        var emissionType = settings?.EmissionType
            ?? _sriOptions.EmissionType;

        if (environment is not 1 and not 2)
        {
            throw new InvalidOperationException("INVALID_SRI_ENVIRONMENT");
        }

        if (emissionType != 1)
        {
            throw new InvalidOperationException("INVALID_SRI_EMISSION_TYPE");
        }

        if (environment == 2 && !_sriOptions.AllowProductionSubmission)
        {
            throw new InvalidOperationException(
                "SRI_PRODUCTION_SUBMISSION_DISABLED");
        }

        return (environment, emissionType);
    }

    private async Task PersistFailedAttemptAsync(
        CreditNote creditNote,
        OperationalContext operationalContext,
        int environment,
        string errorCode,
        string errorMessage)
    {
        try
        {
            var now = _businessClockService.UtcNow;

            await using var transaction =
                await _context.Database.BeginTransactionAsync();
            var originalSaleId = await GetOriginalSaleIdAsync(
                creditNote.Id,
                operationalContext);
            await LockOriginalSaleAsync(originalSaleId, operationalContext);
            var trackedCreditNote = await LockCreditNoteAsync(
                creditNote.Id,
                operationalContext);

            var attempt = BuildBaseAttempt(
                trackedCreditNote,
                operationalContext.UserId,
                environment,
                now);
            attempt.Status = SriSubmissionAttemptStatus.Failed;
            attempt.ErrorCode = errorCode;
            attempt.ErrorMessage = Truncate(errorMessage, 1000);

            if (!IsAlreadyReceived(trackedCreditNote)
                && trackedCreditNote.DocumentStatus
                    != SaleDocumentStatus.Authorized)
            {
                trackedCreditNote.SriLastSubmissionError =
                    Truncate(errorMessage, 1000);
                trackedCreditNote.UpdatedAt = now;
            }

            _context.SriSubmissionAttempts.Add(attempt);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not persist failed SRI credit note attempt. CreditNoteId {CreditNoteId} ErrorCode {ErrorCode}",
                creditNote.Id,
                errorCode);
        }
    }

    private static SriSubmissionAttempt BuildBaseAttempt(
        CreditNote creditNote,
        int userId,
        int environment,
        DateTime createdAt)
        => new()
        {
            SaleId = null,
            CreditNoteId = creditNote.Id,
            CompanyId = creditNote.CompanyId,
            EstablishmentId = creditNote.EstablishmentId,
            EmissionPointId = creditNote.EmissionPointId,
            AccessKey = creditNote.AccessKey ?? string.Empty,
            Environment = environment,
            AttemptType = SriSubmissionAttemptType.Reception,
            Status = SriSubmissionAttemptStatus.Pending,
            CreatedAt = createdAt,
            CreatedByUserId = userId
        };

    private static void ApplyMessage(
        SriSubmissionAttempt attempt,
        SriResponseMessage? message)
    {
        if (message is null)
        {
            return;
        }

        attempt.SriMessageIdentifier = Truncate(message.Identifier, 100);
        attempt.SriMessageType = Truncate(message.Type, 100);
        attempt.SriMessage = Truncate(message.Message, 1000);
        attempt.SriAdditionalInfo = Truncate(message.AdditionalInfo, 2000);
        attempt.ErrorMessage = Truncate(
            message.Message ?? message.AdditionalInfo,
            1000);
    }

    private async Task<CreditNoteDto> GetCurrentCreditNoteAsync(
        int creditNoteId)
        => await _creditNoteService.GetByIdAsync(creditNoteId);

    private static bool IsReceptionExternalError(string code)
        => code is "SRI_RECEPTION_ENDPOINT_NOT_CONFIGURED"
            or "SRI_RECEPTION_COMMUNICATION_FAILED";

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
