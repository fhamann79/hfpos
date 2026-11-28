using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class FiscalRecoveryEvidence
{
    internal const string Uncertain = "FISCAL_RECOVERY_UNCERTAIN";
    internal const string NotEligible = "FISCAL_RECOVERY_NOT_ELIGIBLE";
    internal const string Consumed = "FISCAL_RECOVERY_CONSUMED";
    internal const string Received = "FISCAL_RECOVERY_RECEIVED";
    internal const string Processing = "FISCAL_RECOVERY_PROCESSING";
    internal const string Reconcile = "FISCAL_RECOVERY_RECONCILE_KEY";
    internal const string SequenceConflict = "FISCAL_RECOVERY_SEQUENCE_CONFLICT";
    internal const string Inconclusive = "FISCAL_RECOVERY_CONSULT_INCONCLUSIVE";
    internal const string AuthorizationUnverified = "FISCAL_RECOVERY_AUTHORIZATION_UNVERIFIED";
    internal const string NoRecords = "FISCAL_RECOVERY_NO_AUTHORIZATION_RECORDS";
    internal const string ValidityNoRecords = "FISCAL_RECOVERY_NO_VALIDITY_RECORD";
    internal const string ValidityRecord = "FISCAL_RECOVERY_VALIDITY_RECORD";
    internal const string ValidityInconclusive = "FISCAL_RECOVERY_VALIDITY_INCONCLUSIVE";
    internal const string OutcomeUnknown = "FISCAL_RECOVERY_OUTCOME_UNKNOWN";

    internal static string? SpecialCode(IEnumerable<SriResponseMessage> messages, string? explicitStatus = null)
    {
        var ids = messages.Select(m => m.Identifier?.Trim()).ToHashSet(StringComparer.Ordinal);
        return ids.Contains("45") ? SequenceConflict : ids.Contains("70") || IsProcessing(explicitStatus) ? Processing
            : ids.Contains("43") ? Reconcile : null;
    }

    internal static string? MessageId(string? code) => code switch
    {
        SequenceConflict => "45",
        Processing => "70",
        Reconcile => "43",
        _ => null
    };

    private static bool IsProcessing(string? value) => value?.Trim().ToUpperInvariant()
        is "RECIBIDA" or "EN PROCESAMIENTO" or "PROCESANDO" or "PROCESAMIENTO" or "EN PROCESO" or "PENDIENTE";

    internal static string? ResponseSignal(SriAuthorizationResponse response, bool includeProcessingState = true)
    {
        var messages = SpecialCode(response.Messages);
        var raw = ReadRawSignal(response.RawResponseXml, includeProcessingState);
        return messages == SequenceConflict || raw == SequenceConflict ? SequenceConflict
            : messages == Processing || raw == Processing ? Processing : messages ?? raw;
    }

    internal static bool IsOriginalAuthorizationRecord(SriAuthorizationResponse response, Sale sale)
    {
        var environmentMatches = (sale.SriEnvironment, response.AuthorizationEnvironment?.Trim().ToUpperInvariant()) switch
        {
            (1, "PRUEBAS" or "1") or (2, "PRODUCCIÓN" or "PRODUCCION" or "2") => true,
            _ => false
        };
        var authorizationNumberMatches = response.AuthorizationNumber == sale.AccessKey
            || response.IsRejected && string.IsNullOrEmpty(response.AuthorizationNumber);
        if (response.ConsultedAccessKey != sale.AccessKey || !authorizationNumberMatches
            || !response.HasSingleAuthorizationRecord || !environmentMatches
            || string.IsNullOrWhiteSpace(response.AuthorizedXml) || string.IsNullOrWhiteSpace(sale.SriSignedXml))
            return false;
        try
        {
            // Ignore XML declaration and outer formatting; compare the complete signed invoice, including its signature.
            return XNode.DeepEquals(XDocument.Parse(response.AuthorizedXml, LoadOptions.PreserveWhitespace).Root,
                XDocument.Parse(sale.SriSignedXml, LoadOptions.PreserveWhitespace).Root);
        }
        catch (XmlException) { return false; }
    }

    private static string? ReadRawSignal(string? rawXml, bool includeProcessingState = true)
    {
        if (string.IsNullOrWhiteSpace(rawXml)) return null;
        try
        {
            var xml = XDocument.Parse(rawXml);
            var ids = xml.Descendants().Where(e => e.Name.LocalName == "identificador")
                .Select(e => e.Value.Trim()).ToHashSet(StringComparer.Ordinal);
            return ids.Contains("45") ? SequenceConflict : ids.Contains("70") ? Processing : ids.Contains("43") ? Reconcile
                : includeProcessingState && xml.Descendants().Any(e => e.Name.LocalName == "estado" && IsProcessing(e.Value))
                    ? Processing : null;
        }
        catch (XmlException) { return null; }
    }

    internal static IQueryable<SriSubmissionAttempt> AttemptsFor(PosDbContext db, Sale sale)
        => db.SriSubmissionAttempts.Where(a => a.SaleId == sale.Id && a.CompanyId == sale.CompanyId
            && a.EstablishmentId == sale.EstablishmentId && a.EmissionPointId == sale.EmissionPointId
            && a.AccessKey == sale.AccessKey && a.Environment == sale.SriEnvironment);

    internal static Task<SriSubmissionAttempt?> FindAdmissionAsync(PosDbContext db, Sale sale, CancellationToken ct)
        => db.SriSubmissionAttempts.SingleOrDefaultAsync(a => a.SaleId == sale.Id && a.CompanyId == sale.CompanyId
            && a.AttemptType == SriSubmissionAttemptType.ExtraordinaryReception, ct);

    internal static async Task<string?> HistoricalSignalAsync(PosDbContext db, Sale sale, CancellationToken ct)
    {
        string? signal = null;
        // Legacy audit columns retained only the first message. Stream this document's evidence so a later
        // empty consultation cannot hide a duplicate or processing code anywhere in an older response.
        var attempts = AttemptsFor(db, sale).AsNoTracking().Select(a => new
        {
            a.AttemptType, a.ReceptionStatus, a.AuthorizationStatus, a.SriMessageIdentifier, a.ErrorCode, a.ResponseXml
        });
        await foreach (var attempt in attempts.AsAsyncEnumerable().WithCancellation(ct))
        {
            var raw = ReadRawSignal(attempt.ResponseXml);
            var known = new[] { raw, attempt.ErrorCode };
            if (attempt.SriMessageIdentifier?.Trim() == "45" || known.Contains(SequenceConflict))
                return SequenceConflict;
            if (attempt.ErrorCode == ValidityRecord || attempt.AttemptType == SriSubmissionAttemptType.Validity
                && attempt.ResponseXml is { } validityXml
                && SriWebServiceClient.ParseValidityResponse(validityXml, sale.AccessKey!).Evidence == SriValidityEvidence.RecordExists)
                signal = ValidityRecord;
            if (signal == ValidityRecord) continue;
            if (attempt.ErrorCode == AuthorizationUnverified)
                signal = AuthorizationUnverified;
            if (signal == AuthorizationUnverified) continue;
            if (attempt.ReceptionStatus?.Trim().ToUpperInvariant() == "RECIBIDA"
                || (raw is null && attempt.SriMessageIdentifier?.Trim() is not ("43" or "70")
                    && attempt.AuthorizationStatus?.Trim().ToUpperInvariant() is "AUTORIZADO" or "NO AUTORIZADO"))
                signal = Received;
            else if (signal != Received && (attempt.SriMessageIdentifier?.Trim() == "70" || known.Contains(Processing)
                || IsProcessing(attempt.ReceptionStatus)))
                signal = Processing;
            else if (signal is null && (attempt.SriMessageIdentifier?.Trim() == "43" || known.Contains(Reconcile)))
                signal = Reconcile;
        }
        return signal;
    }

    internal static async Task<string> GetStatusAsync(PosDbContext db, Sale sale, DateTime? originalAdmission,
        CancellationToken ct)
    {
        if (sale.Status != SaleStatus.Completed || sale.DocumentType != SaleDocumentType.Invoice
            || !originalAdmission.HasValue || string.IsNullOrWhiteSpace(sale.SriSignedXml)
            || sale.AccessKey is not { Length: 49 } || sale.SriEnvironment is not (1 or 2))
            return NotEligible;
        var special = await HistoricalSignalAsync(db, sale, ct);
        if (special is SequenceConflict or Processing or Reconcile or Received or ValidityRecord or AuthorizationUnverified)
            return special;
        var admission = await FindAdmissionAsync(db, sale, ct);
        if (admission is not null)
            return admission.RecoveryCompletedAt is null
                || admission.ErrorCode is "SRI_RECEPTION_COMMUNICATION_FAILED" or OutcomeUnknown ? OutcomeUnknown : Consumed;
        if (sale.SriSubmittedAt.HasValue || IsProcessing(sale.SriReceptionStatus)
            || sale.DocumentStatus == SaleDocumentStatus.Authorized || !string.IsNullOrWhiteSpace(sale.AuthorizationNumber)
            || sale.SriAuthorizationStatus?.Trim().ToUpperInvariant() is "AUTORIZADO" or "NO AUTORIZADO")
            return Received;
        if (sale.SriAuthorizationStatus?.Trim().ToUpperInvariant() != "PENDIENTE" && IsProcessing(sale.SriAuthorizationStatus))
            return Processing;
        if (sale.DocumentStatus == SaleDocumentStatus.Rejected
            || sale.SriReceptionStatus?.Trim().ToUpperInvariant() is not (null or "" or "IN_FLIGHT")
            || await AttemptsFor(db, sale).AnyAsync(a => a.AttemptType == SriSubmissionAttemptType.Reception
                && (a.Status == SriSubmissionAttemptStatus.Success || a.ReceptionStatus == "DEVUELTA"), ct))
            return NotEligible;
        return Uncertain;
    }
}
