using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;

namespace Pos.Backend.Api.Infrastructure.Services;

public partial class SriSubmissionService
{
    public async Task<SriReceptionRecoveryStatus> GetReceptionRecoveryStatusAsync(int saleId, CancellationToken ct)
    {
        var actor = await _operationalContextAccessor.GetRequiredContextAsync();
        await ElectronicIssuingCoordinator.RequirePermissionsAsync(_context, actor.UserId, actor.CompanyId,
            [AppPermissions.SriDocumentsSubmit]);
        var sale = await LoadSaleSnapshotAsync(saleId, actor);
        var job = await _context.ElectronicIssuingJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.SaleId == sale.Id && j.CompanyId == actor.CompanyId, ct);
        var evidenceAt = await _coordinator.ResolveReceptionEvidenceAtAsync(sale, job, ct);
        return new(await FiscalRecoveryEvidence.GetStatusAsync(_context, sale, evidenceAt, ct));
    }

    public async Task<SriReceptionRecoveryStatus> RecoverReceptionAsync(int saleId, SriReceptionRecoveryRequest request, CancellationToken ct)
    {
        var actor = await _operationalContextAccessor.GetRequiredContextAsync();
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var reason = request.Reason?.Trim();
        if (!request.ConfirmsResendDespiteUncertainReception || reference is { Length: > 160 }
            || reference?.Any(char.IsControl) == true || reason is not { Length: >= 3 and <= 1000 }
            || reason.Count(char.IsLetterOrDigit) < 3
            || reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new InvalidOperationException("FISCAL_RECOVERY_CONFIRMATION_AND_REASON_REQUIRED");

        var admission = await _coordinator.AdmitAsync(saleId, ElectronicIssuingPhase.AwaitingAuthorization,
            actor, null, ct, recoveryProbe: true);
        var original = admission.Sale;
        var key = original.AccessKey!;
        var environment = original.SriEnvironment!.Value;
        var firstProbe = Stopwatch.GetTimestamp();
        var evidenceId = Guid.NewGuid();

        SriAuthorizationResponse authorization;
        try
        {
            authorization = await _sriWebServiceClient.CheckAuthorizationAsync(key, environment, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _coordinator.FailAsync(admission, ex is InvalidOperationException ? ex.Message
                : "SRI_AUTHORIZATION_COMMUNICATION_FAILED", true, ct);
            throw;
        }

        var authorizationCheckedAt = await _coordinator.DatabaseTimeAsync(ct);
        var authorizationEmpty = authorization.HasExplicitEmptyResult && authorization.ConsultedAccessKey == key
            && authorization.Messages.Count == 0 && !authorization.IsAuthorized && !authorization.IsRejected
            && FiscalRecoveryEvidence.ResponseSignal(authorization) is null;
        SriValidityResponse? validity = null;
        string? validityFailure = null;
        DateTime? validityCheckedAt = null;
        if (authorizationEmpty)
        {
            if (!await _coordinator.RefreshRecoveryLeaseAsync(admission, ct))
                throw new InvalidOperationException("FISCAL_LEASE_LOST");
            try { validity = await _sriWebServiceClient.CheckValidityAsync(key, environment, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                validity = new(SriValidityEvidence.Inconclusive);
                validityFailure = ex is InvalidOperationException { Message: "SRI_VALIDITY_ENDPOINT_NOT_CONFIGURED" }
                    ? "SRI_VALIDITY_ENDPOINT_NOT_CONFIGURED" : "SRI_VALIDITY_COMMUNICATION_FAILED";
            }
            validityCheckedAt = await _coordinator.DatabaseTimeAsync(ct);
        }

        var exactValidity = validity?.ConsultedAccessKey == key ? validity.Evidence : SriValidityEvidence.Inconclusive;
        var canAdmit = authorizationEmpty && exactValidity == SriValidityEvidence.NoRecord;
        string? disposition = null;
        await _coordinator.CompleteAsync(admission, async (sale, job) =>
        {
            var currentEvidence = await FiscalRecoveryEvidence.GetStatusAsync(_context, sale, job.ReceptionStartedAt, ct);
            if (currentEvidence != FiscalRecoveryEvidence.Uncertain)
                throw new InvalidOperationException(currentEvidence);

            disposition = ApplyAuthorizationResponse(sale, job, admission, authorization, environment,
                requireProvenResult: true, recoveryEvidenceId: evidenceId, checkedAt: authorizationCheckedAt);
            if (validity is null) return;
            disposition = exactValidity switch
            {
                SriValidityEvidence.NoRecord => FiscalRecoveryEvidence.ValidityNoRecords,
                SriValidityEvidence.RecordExists => FiscalRecoveryEvidence.ValidityRecord,
                _ => FiscalRecoveryEvidence.ValidityInconclusive
            };
            var validityAttempt = BuildBaseAttempt(sale, admission.AuditUserId, environment,
                SriSubmissionAttemptType.Validity, validityCheckedAt!.Value);
            validityAttempt.Status = exactValidity == SriValidityEvidence.Inconclusive
                ? SriSubmissionAttemptStatus.Failed : SriSubmissionAttemptStatus.Success;
            validityAttempt.AuthorizationStatus = exactValidity == SriValidityEvidence.NoRecord ? "SIN_REGISTROS" : validity.Status;
            validityAttempt.SriMessageIdentifier = validity.MessageIdentifier;
            validityAttempt.ResponseXml = validity.RawResponseXml;
            validityAttempt.ErrorCode = disposition;
            validityAttempt.RecoveryEvidenceId = evidenceId;
            validityAttempt.ErrorMessage = validityFailure;
            _context.SriSubmissionAttempts.Add(validityAttempt);
            if (canAdmit) job.State = ElectronicIssuingJobState.Processing;
        }, ct, recoveryActor: actor, keepLease: canAdmit);

        if (!canAdmit) return new(disposition ?? "FISCAL_RECOVERY_AUTHORIZED");

        // Commit each consultation first, then consume the budget under the same fenced reservation.
        await _coordinator.CompleteAsync(admission, async (sale, job) =>
        {
            var currentEvidence = await FiscalRecoveryEvidence.GetStatusAsync(_context, sale, job.ReceptionStartedAt, ct);
            if (currentEvidence != FiscalRecoveryEvidence.Uncertain)
                throw new InvalidOperationException(currentEvidence);
            var freshAfter = (await _coordinator.DatabaseTimeAsync(ct)).Subtract(ElectronicIssuingCoordinator.LeaseDuration);
            var evidence = await FiscalRecoveryEvidence.AttemptsFor(_context, sale).Where(a =>
                a.RecoveryEvidenceId == evidenceId && a.CreatedByUserId == admission.AuditUserId
                && a.CreatedAt > freshAfter
                && ((a.AttemptType == SriSubmissionAttemptType.Authorization && a.ErrorCode == FiscalRecoveryEvidence.NoRecords)
                    || (a.AttemptType == SriSubmissionAttemptType.Validity && a.ErrorCode == FiscalRecoveryEvidence.ValidityNoRecords)))
                .Select(a => a.AttemptType).Distinct().CountAsync(ct);
            if (evidence != 2)
                throw new InvalidOperationException(FiscalRecoveryEvidence.ValidityInconclusive);
            FiscalDocumentGuard.BeforeReception(sale);
            ValidateUnsignedDraftIfPresent(sale);
            var attempt = BuildBaseAttempt(sale, admission.AuditUserId, environment,
                SriSubmissionAttemptType.ExtraordinaryReception, DateTime.UtcNow);
            attempt.RecoveryReference = reference;
            attempt.RecoveryReason = reason;
            attempt.RecoverySignedXmlHash = ElectronicIssuingCoordinator.Hash(sale.SriSignedXml);
            attempt.RecoveryEvidenceId = evidenceId;
            attempt.ErrorCode = FiscalRecoveryEvidence.OutcomeUnknown;
            _context.SriSubmissionAttempts.Add(attempt);
            job.State = ElectronicIssuingJobState.Processing;
            job.Phase = ElectronicIssuingPhase.UnknownReception; // An expired claim may only consult, never dispatch.
            job.SafeError = FiscalRecoveryEvidence.OutcomeUnknown;
        }, ct, recoveryActor: actor, keepLease: true, requireReceptionPolicy: true);

        // The budget is spent even if this process is terminated before dispatch or before persisting the result.
        SriReceptionResponse reception;
        var dispatchStarted = false;
        try
        {
            reception = await _coordinator.DispatchRecoveryAsync(admission, authorizationCheckedAt, () =>
            {
                if (Stopwatch.GetElapsedTime(firstProbe) >= ElectronicIssuingCoordinator.LeaseDuration)
                    throw new InvalidOperationException(FiscalRecoveryEvidence.ValidityInconclusive);
                dispatchStarted = true;
                return _sriWebServiceClient.SubmitAsync(original.SriSignedXml!, environment, ct);
            }, ct);
        }
        catch (Exception ex) when (dispatchStarted && ex is not OperationCanceledException)
        {
            await _coordinator.CompleteAsync(admission, async (sale, job) =>
            {
                var attempt = await GetExtraordinaryAdmissionAsync(admission, sale, job, ct);
                attempt.Status = SriSubmissionAttemptStatus.Failed;
                attempt.ErrorCode = "SRI_RECEPTION_COMMUNICATION_FAILED";
                attempt.RecoveryCompletedAt = DateTime.UtcNow;
                sale.SriLastSubmissionError = FiscalRecoveryEvidence.OutcomeUnknown;
                ElectronicIssuingCoordinator.Schedule(job, ElectronicIssuingJobState.TransientFailure, ElectronicIssuingPhase.UnknownReception);
                job.SafeError = FiscalRecoveryEvidence.OutcomeUnknown;
            }, ct);
            return new(FiscalRecoveryEvidence.OutcomeUnknown);
        }

        await _coordinator.CompleteAsync(admission, async (sale, job) =>
        {
            var attempt = await GetExtraordinaryAdmissionAsync(admission, sale, job, ct);
            attempt.RecoveryCompletedAt = DateTime.UtcNow;
            disposition = ApplyReceptionResponse(sale, job, attempt, reception, attempt.RecoveryCompletedAt.Value);
        }, ct);
        return new(disposition ?? FiscalRecoveryEvidence.Received);
    }

    private async Task<SriSubmissionAttempt> GetExtraordinaryAdmissionAsync(ElectronicIssuingCoordinator.Admission admission,
        Sale sale, ElectronicIssuingJob job, CancellationToken ct)
    {
        ElectronicIssuingCoordinator.ValidateRecoveryIdentity(admission, sale, job);
        var attempt = await FiscalRecoveryEvidence.FindAdmissionAsync(_context, sale, ct);
        if (attempt is null || attempt.RecoveryCompletedAt.HasValue
            || attempt.RecoverySignedXmlHash != ElectronicIssuingCoordinator.Hash(sale.SriSignedXml))
            throw new InvalidOperationException("FISCAL_DOCUMENT_CONTEXT_CHANGED");
        return attempt;
    }

    private static SriResponseMessage? SelectMessage(IReadOnlyList<SriResponseMessage> messages, string? code)
        => messages.FirstOrDefault(m => m.Identifier?.Trim() == FiscalRecoveryEvidence.MessageId(code))
            ?? messages.FirstOrDefault();

    private static string? ApplyReceptionResponse(Sale sale, ElectronicIssuingJob job, SriSubmissionAttempt attempt,
        SriReceptionResponse response, DateTime now)
    {
        var special = FiscalRecoveryEvidence.SpecialCode(response.Messages, response.IsReceived ? null : response.Estado);
        var received = response.IsReceived && special != FiscalRecoveryEvidence.SequenceConflict;
        var rejected = response.IsReturned && special is null;
        attempt.Status = received ? SriSubmissionAttemptStatus.Success
            : rejected || special == FiscalRecoveryEvidence.SequenceConflict ? SriSubmissionAttemptStatus.Failed
            : SriSubmissionAttemptStatus.Pending;
        attempt.ReceptionStatus = response.Estado;
        attempt.ResponseXml = response.RawResponseXml;
        ApplyMessage(attempt, SelectMessage(response.Messages, special));
        sale.SriReceptionStatus = response.Estado;
        if (received) sale.SriSubmittedAt ??= now;

        var error = special ?? (received ? null : rejected ? "SRI_RECEPTION_REJECTED"
            : attempt.AttemptType == SriSubmissionAttemptType.ExtraordinaryReception
                ? FiscalRecoveryEvidence.OutcomeUnknown : "SRI_AUTHORIZATION_PENDING");
        attempt.ErrorCode = error;
        sale.SriLastSubmissionError = received && error is null ? null : special ?? response.ErrorSummary
            ?? (rejected ? "Comprobante devuelto por SRI." : error);
        if (rejected) attempt.ErrorMessage ??= sale.SriLastSubmissionError;
        sale.DocumentStatus = rejected ? SaleDocumentStatus.Rejected : SaleDocumentStatus.PendingAuthorization;

        ElectronicIssuingCoordinator.Schedule(job,
            rejected ? ElectronicIssuingJobState.Rejected : special == FiscalRecoveryEvidence.SequenceConflict
                ? ElectronicIssuingJobState.ManualAttention : ElectronicIssuingJobState.WaitingAuthorization,
            rejected ? ElectronicIssuingPhase.Completed
                : received || special is FiscalRecoveryEvidence.Reconcile or FiscalRecoveryEvidence.Processing
                    ? ElectronicIssuingPhase.AwaitingAuthorization : ElectronicIssuingPhase.UnknownReception);
        job.SafeError = error;
        return error;
    }
}
