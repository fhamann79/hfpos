using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

// All writers admit and complete in Company -> Sale -> Job order. Claiming is job-only.
public sealed class ElectronicIssuingCoordinator(PosDbContext context, IOptions<SriOptions> options) : IElectronicIssuingRecoveryService
{
    public const int MaximumAttempts = 12;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    internal sealed record Admission(Sale Sale, long JobId, Guid Owner, long Fence, int AuditUserId,
        long? DelegationId, ElectronicIssuingPhase Phase);

    internal static ElectronicIssuingJob NewJob(Sale sale, int originUserId, DateTime now, long? delegationId)
        => new()
        {
            Sale = sale, CompanyId = sale.CompanyId, EstablishmentId = sale.EstablishmentId,
            EmissionPointId = sale.EmissionPointId, UserId = originUserId,
            FiscalDelegationId = delegationId, AutomaticRequested = delegationId.HasValue,
            AccessKey = sale.AccessKey ?? string.Empty, Environment = sale.SriEnvironment ?? 1,
            DraftHash = Hash(sale.SriXmlDraft), CreatedAt = now, UpdatedAt = now, NextAttemptAt = now,
            Phase = sale.DocumentStatus is SaleDocumentStatus.Authorized or SaleDocumentStatus.Rejected
                ? ElectronicIssuingPhase.Completed
                : sale.SriSubmittedAt.HasValue || !string.IsNullOrEmpty(sale.SriReceptionStatus)
                    ? ElectronicIssuingPhase.UnknownReception
                    : string.IsNullOrEmpty(sale.SriSignedXml)
                        ? ElectronicIssuingPhase.ReadyToSign : ElectronicIssuingPhase.ReadyToSubmit,
            State = sale.DocumentStatus == SaleDocumentStatus.Authorized ? ElectronicIssuingJobState.Authorized
                : sale.DocumentStatus == SaleDocumentStatus.Rejected ? ElectronicIssuingJobState.Rejected
                : ElectronicIssuingJobState.Queued,
            ReceptionStartedAt = sale.SriSubmittedAt ?? (!string.IsNullOrEmpty(sale.SriReceptionStatus) ? now : null)
        };

    public async Task<IReadOnlyList<ElectronicIssuingJob>> ClaimBatchAsync(int batchSize, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var size = Math.Clamp(batchSize, 1, 8);
        // The transaction ends before any company/authorization/document lock is acquired.
        return await context.ElectronicIssuingJobs.FromSqlInterpolated($@"
            WITH candidates AS (
                SELECT ""Id"" FROM ""ElectronicIssuingJobs""
                WHERE ""AutomaticRequested"" AND ""State"" IN (0,1,2,3)
                  AND ""NextAttemptAt"" <= clock_timestamp()
                  AND (""LeaseExpiresAt"" IS NULL OR ""LeaseExpiresAt"" <= clock_timestamp())
                ORDER BY ""NextAttemptAt"", ""Id"" LIMIT {size} FOR UPDATE SKIP LOCKED
            )
            UPDATE ""ElectronicIssuingJobs"" j SET
                ""LeaseToken"" = {owner}, ""Fence"" = j.""Fence"" + 1,
                ""LeaseExpiresAt"" = clock_timestamp() + interval '2 minutes',
                ""State"" = 1, ""AttemptCount"" = j.""AttemptCount"" + 1,
                ""Phase"" = CASE WHEN j.""Phase"" = 2 THEN 3 ELSE j.""Phase"" END,
                ""UpdatedAt"" = clock_timestamp()
            FROM candidates c WHERE j.""Id"" = c.""Id"" RETURNING j.*")
            .AsNoTracking().ToListAsync(ct);
    }

    public async Task<bool> HeartbeatAsync(ElectronicIssuingJob claim, CancellationToken ct)
        => await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""ElectronicIssuingJobs"" SET ""LeaseExpiresAt"" = clock_timestamp() + interval '2 minutes'
            WHERE ""Id"" = {claim.Id} AND ""LeaseToken"" = {claim.LeaseToken}
              AND ""Fence"" = {claim.Fence} AND ""LeaseExpiresAt"" > clock_timestamp()", ct) == 1;

    internal async Task<Admission> AdmitAsync(int saleId, ElectronicIssuingPhase purpose,
        OperationalContext? manual, ElectronicIssuingJob? claim, CancellationToken ct)
    {
        context.ChangeTracker.Clear();
        var companyId = manual?.CompanyId ?? claim?.CompanyId ?? throw new InvalidOperationException("FISCAL_AUTHORITY_REQUIRED");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {companyId} FOR SHARE", ct);
        if (manual is not null)
        {
            await new TenantAdministrationGuard(context).LockOperationalWriteAsync(manual);
            await RequirePermissionsAsync(context, manual.UserId, companyId,
                purpose == ElectronicIssuingPhase.ReadyToSign ? [AppPermissions.SriDocumentsSign] : [AppPermissions.SriDocumentsSubmit]);
        }
        var sale = await context.Sales.FromSqlInterpolated($@"
            SELECT * FROM ""Sales"" WHERE ""Id"" = {saleId} AND ""CompanyId"" = {companyId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        if (manual is not null && (sale.EstablishmentId != manual.EstablishmentId || sale.EmissionPointId != manual.EmissionPointId))
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        var job = await context.ElectronicIssuingJobs.FromSqlInterpolated($@"
            SELECT * FROM ""ElectronicIssuingJobs"" WHERE ""SaleId"" = {saleId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (purpose == ElectronicIssuingPhase.AwaitingAuthorization)
            await ValidateAuthorizationEvidenceAsync(sale, job, ct);
        if (job is null)
        {
            if (claim is not null) throw new InvalidOperationException("FISCAL_LEASE_LOST");
            job = NewJob(sale, sale.UserId, DateTime.UtcNow, null);
            context.ElectronicIssuingJobs.Add(job);
        }
        if (claim is not null)
        {
            if (claim.Id != job.Id || claim.CompanyId != sale.CompanyId || claim.EstablishmentId != sale.EstablishmentId
                || claim.EmissionPointId != sale.EmissionPointId)
                throw new InvalidOperationException("FISCAL_LEASE_LOST");
            await VerifyOwnerAsync(job, claim.LeaseToken!.Value, claim.Fence, ct);
        }
        else
        {
            if (await LeaseIsLiveAsync(job.Id, ct)) throw new InvalidOperationException("FISCAL_DOCUMENT_BUSY");
            job.LeaseToken = Guid.NewGuid(); job.Fence++; job.AttemptCount++;
            job.LeaseExpiresAt = DateTime.UtcNow.Add(LeaseDuration);
        }
        var auditUserId = manual?.UserId;
        if (claim is not null)
        {
            var delegation = await context.FiscalDelegations.AsNoTracking().SingleOrDefaultAsync(d =>
                d.Id == job.FiscalDelegationId && d.CompanyId == companyId && d.DisabledAt == null, ct);
            var delegatedSettings = await context.CompanySriSettings.AsNoTracking().SingleOrDefaultAsync(s => s.CompanyId == companyId, ct);
            if (delegation is null || delegatedSettings?.AutomaticProcessingEnabled != true
                || delegatedSettings.AutomaticProcessingRevision != delegation.Revision)
                throw new InvalidOperationException("FISCAL_DELEGATION_REVOKED");
            auditUserId = delegation.EnabledByUserId;
        }
        await ValidateCurrentPolicyAsync(sale, purpose, ct, claim is not null);
        if (job.CompanyId != sale.CompanyId || job.EstablishmentId != sale.EstablishmentId
            || job.EmissionPointId != sale.EmissionPointId || job.AccessKey != sale.AccessKey
            || job.Environment != sale.SriEnvironment || job.DraftHash != Hash(sale.SriXmlDraft))
            throw new InvalidOperationException("FISCAL_DOCUMENT_CONTEXT_CHANGED");
        if (job.AttemptCount > MaximumAttempts && claim is not null)
            throw new InvalidOperationException("FISCAL_ATTEMPTS_EXHAUSTED");
        if (purpose == ElectronicIssuingPhase.ReadyToSign)
        {
            if (job.ReceptionStartedAt.HasValue || job.Phase != ElectronicIssuingPhase.ReadyToSign)
                throw new InvalidOperationException("SRI_XML_ALREADY_SIGNED");
        }
        else if (purpose == ElectronicIssuingPhase.ReadyToSubmit)
        {
            if (job.ReceptionStartedAt.HasValue || job.Phase != ElectronicIssuingPhase.ReadyToSubmit)
                throw new InvalidOperationException("FISCAL_RECEPTION_ALREADY_ADMITTED");
            job.ReceptionStartedAt = DateTime.UtcNow;
            job.Phase = ElectronicIssuingPhase.ReceptionInFlight;
            sale.SriReceptionStatus = "IN_FLIGHT";
        }
        job.State = ElectronicIssuingJobState.Processing;
        job.SafeError = null; job.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new Admission(sale, job.Id, job.LeaseToken!.Value, job.Fence,
            auditUserId!.Value, claim is null ? null : job.FiscalDelegationId, purpose);
    }

    internal async Task CompleteAsync(Admission admission, Func<Sale, ElectronicIssuingJob, Task> mutate, CancellationToken ct)
    {
        context.ChangeTracker.Clear();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {admission.Sale.CompanyId} FOR SHARE", ct);
        var sale = await context.Sales.FromSqlInterpolated($"SELECT * FROM \"Sales\" WHERE \"Id\" = {admission.Sale.Id} FOR UPDATE").SingleAsync(ct);
        var job = await context.ElectronicIssuingJobs.FromSqlInterpolated($"SELECT * FROM \"ElectronicIssuingJobs\" WHERE \"Id\" = {admission.JobId} FOR UPDATE").SingleAsync(ct);
        if (job.SaleId != sale.Id || job.CompanyId != sale.CompanyId || job.EstablishmentId != sale.EstablishmentId
            || job.EmissionPointId != sale.EmissionPointId)
            throw new InvalidOperationException("FISCAL_LEASE_LOST");
        await VerifyOwnerAsync(job, admission.Owner, admission.Fence, ct);
        await mutate(sale, job);
        job.UpdatedAt = DateTime.UtcNow; sale.UpdatedAt = job.UpdatedAt;
        await context.SaveChangesAsync(ct);
        // Check the lease at the final write, not just before potentially expensive signing/mutations.
        // A zero-row release rolls back every document, attempt and job write in this transaction.
        var released = await context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""ElectronicIssuingJobs"" SET ""LeaseToken"" = NULL, ""LeaseExpiresAt"" = NULL
            WHERE ""Id"" = {job.Id} AND ""LeaseToken"" = {admission.Owner}
              AND ""Fence"" = {admission.Fence} AND ""LeaseExpiresAt"" > clock_timestamp()", ct);
        if (released != 1) throw new InvalidOperationException("FISCAL_LEASE_LOST");
        await tx.CommitAsync(ct);
        job.LeaseToken = null; job.LeaseExpiresAt = null;
        context.Entry(job).State = EntityState.Unchanged;
    }

    internal Task FailAsync(Admission admission, string code, bool ambiguous, CancellationToken ct)
        => CompleteAsync(admission, (sale, job) =>
        {
            ApplyFailure(job, code, ambiguous);
            if (admission.AuditUserId > 0 && admission.Phase != ElectronicIssuingPhase.ReadyToSign)
            {
                context.SriSubmissionAttempts.Add(new SriSubmissionAttempt
                {
                    SaleId = sale.Id, CompanyId = sale.CompanyId, EstablishmentId = sale.EstablishmentId,
                    EmissionPointId = sale.EmissionPointId, AccessKey = job.AccessKey, Environment = job.Environment,
                    CreatedAt = DateTime.UtcNow, CreatedByUserId = admission.AuditUserId,
                    FiscalDelegationId = admission.DelegationId,
                    AttemptType = admission.Phase == ElectronicIssuingPhase.ReadyToSubmit
                        ? SriSubmissionAttemptType.Reception : SriSubmissionAttemptType.Authorization,
                    Status = SriSubmissionAttemptStatus.Failed, ErrorCode = SafeCode(code), ErrorMessage = SafeCode(code)
                });
                sale.SriLastSubmissionError = SafeCode(code);
            }
            return Task.CompletedTask;
        }, ct);

    public async Task FailClaimAsync(ElectronicIssuingJob claim, string code, CancellationToken ct)
    {
        // A failed admission still owns a reservation, but is not permission to start a fiscal action.
        var sale = await context.Sales.AsNoTracking().SingleAsync(s => s.Id == claim.SaleId, ct);
        await FailAsync(new Admission(sale, claim.Id, claim.LeaseToken!.Value, claim.Fence, 0, claim.FiscalDelegationId, claim.Phase), code,
            claim.ReceptionStartedAt.HasValue, ct);
    }

    public async Task ResumeAsync(int saleId, OperationalContext actor, CancellationToken ct)
    {
        context.ChangeTracker.Clear();
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await new TenantAdministrationGuard(context).LockOperationalWriteAsync(actor);
        await RequirePermissionsAsync(context, actor.UserId, actor.CompanyId, [AppPermissions.SriDocumentsSubmit]);
        var sale = await context.Sales.FromSqlInterpolated($@"SELECT * FROM ""Sales""
            WHERE ""Id"" = {saleId} AND ""CompanyId"" = {actor.CompanyId}
              AND ""EstablishmentId"" = {actor.EstablishmentId} AND ""EmissionPointId"" = {actor.EmissionPointId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        await ValidateCurrentPolicyAsync(sale, ElectronicIssuingPhase.AwaitingAuthorization, ct, automatic: true);
        if (sale.DocumentStatus is SaleDocumentStatus.Authorized or SaleDocumentStatus.Rejected)
            throw new InvalidOperationException("FISCAL_DOCUMENT_TERMINAL");
        var settings = await context.CompanySriSettings.AsNoTracking().SingleOrDefaultAsync(s => s.CompanyId == actor.CompanyId, ct);
        var delegation = await context.FiscalDelegations.AsNoTracking().SingleOrDefaultAsync(d =>
            d.CompanyId == actor.CompanyId && d.DisabledAt == null, ct);
        if (settings?.AutomaticProcessingEnabled != true || delegation is null || delegation.Revision != settings.AutomaticProcessingRevision)
            throw new InvalidOperationException("FISCAL_DELEGATION_REVOKED");
        var job = await context.ElectronicIssuingJobs.FromSqlInterpolated($"SELECT * FROM \"ElectronicIssuingJobs\" WHERE \"SaleId\" = {saleId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (job is null)
        {
            job = NewJob(sale, sale.UserId, DateTime.UtcNow, delegation.Id);
            context.ElectronicIssuingJobs.Add(job);
        }
        if (await LeaseIsLiveAsync(job.Id, ct)) throw new InvalidOperationException("FISCAL_DOCUMENT_BUSY");
        job.FiscalDelegationId = delegation.Id; job.AutomaticRequested = true; job.Fence++;
        job.LeaseToken = null; job.LeaseExpiresAt = null; job.AttemptCount = 0; job.SafeError = null;
        job.Phase = job.ReceptionStartedAt.HasValue ? ElectronicIssuingPhase.UnknownReception
            : string.IsNullOrEmpty(sale.SriSignedXml) ? ElectronicIssuingPhase.ReadyToSign : ElectronicIssuingPhase.ReadyToSubmit;
        job.State = ElectronicIssuingJobState.Queued; job.NextAttemptAt = DateTime.UtcNow; job.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal static void Schedule(ElectronicIssuingJob job, ElectronicIssuingJobState state, ElectronicIssuingPhase phase)
    {
        job.State = job.AttemptCount >= MaximumAttempts && state is ElectronicIssuingJobState.WaitingAuthorization or ElectronicIssuingJobState.TransientFailure
            ? ElectronicIssuingJobState.ManualAttention : state;
        job.Phase = phase;
        job.NextAttemptAt = DateTime.UtcNow.AddSeconds(state == ElectronicIssuingJobState.Queued ? 0 : Math.Min(900, 5 * Math.Pow(2, Math.Min(8, job.AttemptCount))));
        if (job.State == ElectronicIssuingJobState.ManualAttention) job.SafeError = "FISCAL_ATTEMPTS_EXHAUSTED";
    }

    private static void ApplyFailure(ElectronicIssuingJob job, string code, bool ambiguous)
    {
        job.SafeError = SafeCode(code);
        var transient = code is "SRI_RECEPTION_COMMUNICATION_FAILED" or "SRI_AUTHORIZATION_COMMUNICATION_FAILED" or "FISCAL_TRANSIENT_FAILURE";
        Schedule(job, transient ? ElectronicIssuingJobState.TransientFailure : ElectronicIssuingJobState.ManualAttention,
            ambiguous ? ElectronicIssuingPhase.UnknownReception : job.Phase);
    }

    internal static string SafeCode(string code) => code.Length <= 100 && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
        ? code : "FISCAL_TRANSIENT_FAILURE";

    private async Task<bool> LeaseIsLiveAsync(long id, CancellationToken ct)
        => await context.Database.SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM ""ElectronicIssuingJobs""
            WHERE ""Id"" = {id} AND ""LeaseExpiresAt"" > clock_timestamp()")
            .SingleAsync(ct) == 1;

    private async Task VerifyOwnerAsync(ElectronicIssuingJob job, Guid owner, long fence, CancellationToken ct)
    {
        if (job.LeaseToken != owner || job.Fence != fence || !await LeaseIsLiveAsync(job.Id, ct))
            throw new InvalidOperationException("FISCAL_LEASE_LOST");
    }

    internal static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private async Task ValidateAuthorizationEvidenceAsync(Sale sale, ElectronicIssuingJob? job, CancellationToken ct)
    {
        // Querying is recovery, not permission to skip signing/reception or reopen a terminal job.
        if (job is { Phase: ElectronicIssuingPhase.Completed, State: ElectronicIssuingJobState.Rejected })
            throw new InvalidOperationException("FISCAL_DOCUMENT_TERMINAL");
        if (sale.DocumentStatus == SaleDocumentStatus.Authorized) return;
        var latest = await context.SriSubmissionAttempts.AsNoTracking()
            .Where(a => a.SaleId == sale.Id && a.CompanyId == sale.CompanyId
                && a.AccessKey == sale.AccessKey && a.Environment == sale.SriEnvironment)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.Status, a.ErrorCode, a.ReceptionStatus, a.AuthorizationStatus })
            .FirstOrDefaultAsync(ct);
        if (job is null && sale.DocumentStatus == SaleDocumentStatus.Rejected
            && ((latest?.Status == SriSubmissionAttemptStatus.Failed
                && ((latest.ErrorCode == "SRI_RECEPTION_REJECTED" && latest.ReceptionStatus == "DEVUELTA")
                    || (latest.ErrorCode == "SRI_AUTHORIZATION_REJECTED" && latest.AuthorizationStatus == "NO AUTORIZADO")))
                || (latest is null && (sale.SriReceptionStatus == "DEVUELTA" || sale.SriAuthorizationStatus == "NO AUTORIZADO"))))
            throw new InvalidOperationException("FISCAL_DOCUMENT_TERMINAL");
        if (job?.ReceptionStartedAt.HasValue == true || sale.SriSubmittedAt.HasValue
            || !string.IsNullOrWhiteSpace(sale.SriReceptionStatus)) return;
        // Legacy failed reception attempts also require query-only reconciliation, never a blind resend.
        if (!await context.SriSubmissionAttempts.AnyAsync(a => a.SaleId == sale.Id && a.CompanyId == sale.CompanyId
            && a.AccessKey == sale.AccessKey && a.Environment == sale.SriEnvironment
            && a.AttemptType == SriSubmissionAttemptType.Reception, ct))
            throw new InvalidOperationException("FISCAL_RECEPTION_NOT_ADMITTED");
    }

    internal static async Task RequirePermissionsAsync(PosDbContext db, int userId, int companyId, string[] permissions)
    {
        var allowed = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.CompanyId == companyId && u.IsActive
            && u.Role.CompanyId == companyId && u.Role.IsActive)
            .SelectMany(u => u.Role.RolePermissions).Where(rp => rp.Permission.IsActive && permissions.Contains(rp.Permission.Code))
            .Select(rp => rp.Permission.Code).Distinct().CountAsync();
        if (allowed != permissions.Length) throw new InvalidOperationException("FISCAL_PERMISSION_REQUIRED");
    }

    private async Task ValidateCurrentPolicyAsync(Sale sale, ElectronicIssuingPhase purpose, CancellationToken ct, bool automatic = false)
    {
        if (sale.Status != SaleStatus.Completed || sale.DocumentType != SaleDocumentType.Invoice)
            throw new InvalidOperationException("SRI_SUBMISSION_SALE_VOIDED");
        if (!await context.Companies.AnyAsync(c => c.Id == sale.CompanyId && c.IsActive, ct)
            || !await context.EmissionPoints.AnyAsync(p => p.Id == sale.EmissionPointId && p.IsActive
                && p.EstablishmentId == sale.EstablishmentId && p.Establishment.IsActive && p.Establishment.CompanyId == sale.CompanyId, ct))
            throw new InvalidOperationException("FISCAL_TENANT_INACTIVE");
        var settings = await context.CompanySriSettings.AsNoTracking().SingleOrDefaultAsync(s => s.CompanyId == sale.CompanyId, ct);
        if (settings?.IsEnabled == false) throw new InvalidOperationException("SRI_SETTINGS_DISABLED");
        var environment = sale.SriEnvironment ?? settings?.Environment ?? options.Value.Environment;
        if (environment is not (1 or 2)) throw new InvalidOperationException("INVALID_SRI_ENVIRONMENT");
        if (settings is not null && (settings.Environment != environment || settings.EmissionType != 1))
            throw new InvalidOperationException("FISCAL_DOCUMENT_CONTEXT_CHANGED");
        if (environment == 2 && !options.Value.AllowProductionSubmission)
            throw new InvalidOperationException("SRI_PRODUCTION_SUBMISSION_DISABLED");
        if ((automatic || purpose == ElectronicIssuingPhase.ReadyToSubmit) && !await context.CompanySriCertificates.AnyAsync(c =>
            c.CompanyId == sale.CompanyId && c.IsActive && c.HasPrivateKey && c.NotBefore <= DateTime.UtcNow && c.NotAfter > DateTime.UtcNow, ct))
            throw new InvalidOperationException("CERTIFICATE_NOT_FOUND");
    }
}
