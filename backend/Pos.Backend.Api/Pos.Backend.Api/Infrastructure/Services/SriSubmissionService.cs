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

public class SriSubmissionService : ISriSubmissionService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriWebServiceClient _sriWebServiceClient;
    private readonly ISalesService _salesService;
    private readonly ISriInvoiceXmlValidator _sriInvoiceXmlValidator;
    private readonly SriOptions _sriOptions;
    private readonly ILogger<SriSubmissionService> _logger;

    public SriSubmissionService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriWebServiceClient sriWebServiceClient,
        ISalesService salesService,
        ISriInvoiceXmlValidator sriInvoiceXmlValidator,
        IOptions<SriOptions> sriOptions,
        ILogger<SriSubmissionService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _sriWebServiceClient = sriWebServiceClient;
        _salesService = salesService;
        _sriInvoiceXmlValidator = sriInvoiceXmlValidator;
        _sriOptions = sriOptions.Value;
        _logger = logger;
    }

    public async Task<SaleDto> SubmitSignedInvoiceAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var sale = await LoadSaleSnapshotAsync(saleId, operationalContext);

        ValidateSaleCanBeSubmitted(sale);
        ValidateUnsignedDraftIfPresent(sale);

        var sriContext = await ResolveSriSubmissionContextAsync(sale.CompanyId, sale.SriEnvironment);

        SriReceptionResponse response;

        try
        {
            response = await _sriWebServiceClient.SubmitAsync(sale.SriSignedXml!, sriContext.Environment);
        }
        catch (InvalidOperationException ex) when (IsReceptionExternalError(ex.Message))
        {
            await PersistFailedAttemptAsync(
                sale,
                operationalContext.UserId,
                sriContext.Environment,
                SriSubmissionAttemptType.Reception,
                ex.Message,
                ex.InnerException?.Message ?? ex.Message);
            throw;
        }

        var responseMessage = response.Messages.FirstOrDefault();
        var now = DateTime.UtcNow;
        string? postCommitError = null;

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            var trackedSale = await LockSaleAsync(sale.Id, operationalContext);
            ValidateSaleCanBeSubmitted(trackedSale);
            ValidateUnsignedDraftIfPresent(trackedSale);

            var attempt = BuildBaseAttempt(
                trackedSale,
                operationalContext.UserId,
                sriContext.Environment,
                SriSubmissionAttemptType.Reception,
                now);
            attempt.Status = response.IsReceived
                ? SriSubmissionAttemptStatus.Success
                : SriSubmissionAttemptStatus.Failed;
            attempt.ReceptionStatus = response.Estado;
            attempt.ResponseXml = response.RawResponseXml;
            ApplyMessage(attempt, responseMessage);

            trackedSale.SriReceptionStatus = response.Estado;
            trackedSale.SriSubmittedAt = response.IsReceived ? now : trackedSale.SriSubmittedAt;
            trackedSale.SriLastSubmissionError = response.IsReceived
                ? null
                : response.ErrorSummary ?? "Comprobante devuelto por SRI.";
            trackedSale.UpdatedAt = now;

            if (response.IsReceived)
            {
                trackedSale.DocumentStatus = SaleDocumentStatus.PendingAuthorization;
            }
            else
            {
                trackedSale.DocumentStatus = SaleDocumentStatus.Rejected;
                attempt.ErrorCode = "SRI_RECEPTION_REJECTED";
                attempt.ErrorMessage ??= trackedSale.SriLastSubmissionError;
                postCommitError = "SRI_RECEPTION_REJECTED";
            }

            _context.SriSubmissionAttempts.Add(attempt);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        if (postCommitError is not null)
        {
            throw new InvalidOperationException(postCommitError);
        }

        return await GetSaleDtoOrThrowAsync(sale.Id);
    }

    public async Task<SaleDto> CheckAuthorizationAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var sale = await LoadSaleSnapshotAsync(saleId, operationalContext);

        ValidateSaleCanCheckAuthorization(sale);

        if (sale.DocumentStatus == SaleDocumentStatus.Authorized)
        {
            return await GetSaleDtoOrThrowAsync(sale.Id);
        }

        var sriContext = await ResolveSriSubmissionContextAsync(sale.CompanyId, sale.SriEnvironment);

        SriAuthorizationResponse response;

        try
        {
            response = await _sriWebServiceClient.CheckAuthorizationAsync(sale.AccessKey!, sriContext.Environment);
        }
        catch (InvalidOperationException ex) when (IsAuthorizationExternalError(ex.Message))
        {
            await PersistFailedAttemptAsync(
                sale,
                operationalContext.UserId,
                sriContext.Environment,
                SriSubmissionAttemptType.Authorization,
                ex.Message,
                ex.InnerException?.Message ?? ex.Message);
            throw;
        }

        var responseMessage = response.Messages.FirstOrDefault();
        var now = DateTime.UtcNow;
        string? postCommitError = null;

        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            var trackedSale = await LockSaleAsync(sale.Id, operationalContext);
            ValidateSaleCanCheckAuthorization(trackedSale);

            if (trackedSale.DocumentStatus == SaleDocumentStatus.Authorized)
            {
                await transaction.CommitAsync();
                return await GetSaleDtoOrThrowAsync(trackedSale.Id);
            }

            var attempt = BuildBaseAttempt(
                trackedSale,
                operationalContext.UserId,
                sriContext.Environment,
                SriSubmissionAttemptType.Authorization,
                now);
            attempt.Status = response.IsAuthorized
                ? SriSubmissionAttemptStatus.Success
                : response.IsRejected
                    ? SriSubmissionAttemptStatus.Failed
                    : SriSubmissionAttemptStatus.Pending;
            attempt.AuthorizationStatus = response.Estado;
            attempt.AuthorizationNumber = response.AuthorizationNumber;
            attempt.AuthorizationDate = response.AuthorizationDate;
            attempt.ResponseXml = response.RawResponseXml;
            ApplyMessage(attempt, responseMessage);

            trackedSale.SriAuthorizationStatus = response.Estado;
            trackedSale.SriLastCheckedAt = now;
            trackedSale.UpdatedAt = now;

            if (response.IsAuthorized)
            {
                trackedSale.DocumentStatus = SaleDocumentStatus.Authorized;
                trackedSale.AuthorizationNumber = response.AuthorizationNumber;
                trackedSale.AuthorizedAt = response.AuthorizationDate ?? now;
                trackedSale.SriLastSubmissionError = null;
            }
            else if (response.IsRejected)
            {
                trackedSale.DocumentStatus = SaleDocumentStatus.Rejected;
                trackedSale.SriLastSubmissionError = response.ErrorSummary ?? "Comprobante no autorizado por SRI.";
                attempt.ErrorCode = "SRI_AUTHORIZATION_REJECTED";
                attempt.ErrorMessage ??= trackedSale.SriLastSubmissionError;
                postCommitError = "SRI_AUTHORIZATION_REJECTED";
            }
            else
            {
                trackedSale.SriLastSubmissionError = response.ErrorSummary ?? "Autorización pendiente en SRI.";
                attempt.ErrorCode = "SRI_AUTHORIZATION_PENDING";
                attempt.ErrorMessage ??= trackedSale.SriLastSubmissionError;
                postCommitError = "SRI_AUTHORIZATION_PENDING";
            }

            _context.SriSubmissionAttempts.Add(attempt);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        if (postCommitError is not null)
        {
            throw new InvalidOperationException(postCommitError);
        }

        return await GetSaleDtoOrThrowAsync(sale.Id);
    }

    public async Task<IReadOnlyList<SriSubmissionAttemptDto>> GetAttemptsAsync(int saleId)
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

        return await _context.SriSubmissionAttempts
            .AsNoTracking()
            .Where(a => a.SaleId == saleId
                && a.CompanyId == operationalContext.CompanyId
                && a.EstablishmentId == operationalContext.EstablishmentId
                && a.EmissionPointId == operationalContext.EmissionPointId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new SriSubmissionAttemptDto
            {
                Id = a.Id,
                SaleId = a.SaleId,
                AccessKey = a.AccessKey,
                Environment = a.Environment,
                AttemptType = a.AttemptType,
                Status = a.Status,
                ReceptionStatus = a.ReceptionStatus,
                AuthorizationStatus = a.AuthorizationStatus,
                AuthorizationNumber = a.AuthorizationNumber,
                AuthorizationDate = a.AuthorizationDate,
                ErrorCode = a.ErrorCode,
                ErrorMessage = a.ErrorMessage,
                SriMessageIdentifier = a.SriMessageIdentifier,
                SriMessageType = a.SriMessageType,
                SriMessage = a.SriMessage,
                SriAdditionalInfo = a.SriAdditionalInfo,
                CreatedAt = a.CreatedAt,
                CreatedByUserId = a.CreatedByUserId
            })
            .ToListAsync();
    }

    private async Task<Sale> LoadSaleSnapshotAsync(int saleId, OperationalContext operationalContext)
    {
        var sale = await _context.Sales
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == saleId
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId);

        return sale ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    private async Task<Sale> LockSaleAsync(int saleId, OperationalContext operationalContext)
    {
        var sale = await _context.Sales
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Sales""
                WHERE ""Id"" = {saleId}
                  AND ""CompanyId"" = {operationalContext.CompanyId}
                  AND ""EstablishmentId"" = {operationalContext.EstablishmentId}
                  AND ""EmissionPointId"" = {operationalContext.EmissionPointId}
                FOR UPDATE")
            .SingleOrDefaultAsync();

        return sale ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    private static void ValidateSaleCanBeSubmitted(Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SRI_SUBMISSION_ONLY_INVOICE");
        }

        if (sale.Status == SaleStatus.Voided)
        {
            throw new InvalidOperationException("SRI_SUBMISSION_SALE_VOIDED");
        }

        if (sale.DocumentStatus == SaleDocumentStatus.Authorized)
        {
            throw new InvalidOperationException("SRI_ALREADY_AUTHORIZED");
        }

        if (string.IsNullOrWhiteSpace(sale.SriSignedXml))
        {
            throw new InvalidOperationException("SRI_SIGNED_XML_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(sale.AccessKey))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_REQUIRED");
        }
    }

    private void ValidateUnsignedDraftIfPresent(Sale sale)
    {
        if (!string.IsNullOrWhiteSpace(sale.SriXmlDraft))
        {
            _sriInvoiceXmlValidator.ValidateUnsignedInvoiceXml(sale.SriXmlDraft);
        }
    }

    private static void ValidateSaleCanCheckAuthorization(Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SRI_SUBMISSION_ONLY_INVOICE");
        }

        if (sale.Status == SaleStatus.Voided)
        {
            throw new InvalidOperationException("SRI_SUBMISSION_SALE_VOIDED");
        }

        if (string.IsNullOrWhiteSpace(sale.AccessKey))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_REQUIRED");
        }
    }

    private async Task<(int Environment, int EmissionType)> ResolveSriSubmissionContextAsync(
        int companyId,
        int? saleEnvironment)
    {
        var settings = await _context.CompanySriSettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new { s.Environment, s.EmissionType, s.IsEnabled })
            .FirstOrDefaultAsync();

        if (settings is not null && !settings.IsEnabled)
        {
            throw new InvalidOperationException("SRI_SETTINGS_DISABLED");
        }

        var environment = saleEnvironment ?? settings?.Environment ?? _sriOptions.Environment;
        var emissionType = settings?.EmissionType ?? _sriOptions.EmissionType;

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
            throw new InvalidOperationException("SRI_PRODUCTION_SUBMISSION_DISABLED");
        }

        return (environment, emissionType);
    }

    private async Task PersistFailedAttemptAsync(
        Sale sale,
        int userId,
        int environment,
        SriSubmissionAttemptType attemptType,
        string errorCode,
        string errorMessage)
    {
        try
        {
            var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
            var now = DateTime.UtcNow;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var trackedSale = await LockSaleAsync(sale.Id, operationalContext);

            var attempt = BuildBaseAttempt(trackedSale, userId, environment, attemptType, now);
            attempt.Status = SriSubmissionAttemptStatus.Failed;
            attempt.ErrorCode = errorCode;
            attempt.ErrorMessage = Truncate(errorMessage, 1000);

            trackedSale.SriLastSubmissionError = Truncate(errorMessage, 1000);
            trackedSale.SriLastCheckedAt = attemptType == SriSubmissionAttemptType.Authorization
                ? now
                : trackedSale.SriLastCheckedAt;
            trackedSale.UpdatedAt = now;

            _context.SriSubmissionAttempts.Add(attempt);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not persist failed SRI attempt. SaleId {SaleId} AttemptType {AttemptType} ErrorCode {ErrorCode}",
                sale.Id,
                attemptType,
                errorCode);
        }
    }

    private static SriSubmissionAttempt BuildBaseAttempt(
        Sale sale,
        int userId,
        int environment,
        SriSubmissionAttemptType attemptType,
        DateTime createdAt)
        => new()
        {
            SaleId = sale.Id,
            CompanyId = sale.CompanyId,
            EstablishmentId = sale.EstablishmentId,
            EmissionPointId = sale.EmissionPointId,
            AccessKey = sale.AccessKey ?? string.Empty,
            Environment = environment,
            AttemptType = attemptType,
            Status = SriSubmissionAttemptStatus.Pending,
            CreatedAt = createdAt,
            CreatedByUserId = userId
        };

    private static void ApplyMessage(SriSubmissionAttempt attempt, SriResponseMessage? message)
    {
        if (message is null)
        {
            return;
        }

        attempt.SriMessageIdentifier = Truncate(message.Identifier, 100);
        attempt.SriMessageType = Truncate(message.Type, 100);
        attempt.SriMessage = Truncate(message.Message, 1000);
        attempt.SriAdditionalInfo = Truncate(message.AdditionalInfo, 2000);
        attempt.ErrorMessage = Truncate(message.Message ?? message.AdditionalInfo, 1000);
    }

    private async Task<SaleDto> GetSaleDtoOrThrowAsync(int saleId)
        => await _salesService.GetByIdAsync(saleId)
            ?? throw new KeyNotFoundException("SALE_NOT_FOUND");

    private static bool IsReceptionExternalError(string code)
        => code is "SRI_RECEPTION_ENDPOINT_NOT_CONFIGURED" or "SRI_RECEPTION_COMMUNICATION_FAILED";

    private static bool IsAuthorizationExternalError(string code)
        => code is "SRI_AUTHORIZATION_ENDPOINT_NOT_CONFIGURED" or "SRI_AUTHORIZATION_COMMUNICATION_FAILED";

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
