using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

// Internal server capability, not an HTTP identity or arbitrary tenant impersonation.
public sealed class FiscalTrustMonitor(PosDbContext db, IDataProtectionProvider protection,
    IFiscalCertificateEvaluator evaluator, FiscalPreflightCache cache, FiscalPublicChainCache publicChains,
    IOptions<FiscalTrustOptions> options)
{
    public async Task RunCycleAsync(CancellationToken ct)
    {
        var after = 0;
        while (true)
        {
            var batch = await FiscalTrustSnapshots.Query(db).Where(s => s.CompanyId > after)
                .OrderBy(s => s.CompanyId).Take(Math.Clamp(options.Value.MonitorBatchSize, 1, 50)).ToListAsync(ct);
            if (batch.Count == 0) { cache.Completed(); return; }
            foreach (var snapshot in batch)
            {
                ct.ThrowIfCancellationRequested();
                await ObserveAsync(snapshot, ct);
                after = snapshot.CompanyId;
            }
        }
    }

    private async Task ObserveAsync(FiscalTenantTrustSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            if (snapshot.CertificateId is null)
            { cache.Record(snapshot, null, "CERTIFICATE_NOT_FOUND"); return; }
            var rows = await db.CompanySriCertificates.AsNoTracking().Where(c => c.CompanyId == snapshot.CompanyId)
                .OrderByDescending(c => c.IsActive).ThenByDescending(c => c.Id).Take(33).ToListAsync(ct);
            if (rows.Count > 32) { cache.Record(snapshot, null, "FISCAL_PUBLIC_CHAIN_HISTORY_LIMIT"); return; }
            FiscalTrustDecision? decision = null;
            var protector = protection.CreateProtector(SriCertificateProtectionPurposes.CertificateV1);
            foreach (var row in rows)
            {
                byte[]? pfx = null; byte[]? password = null;
                try
                {
                    pfx = protector.Unprotect(row.EncryptedCertificateBytes);
                    password = protector.Unprotect(row.EncryptedPassword);
                    using var material = SriPfxReader.Load(pfx, Encoding.UTF8.GetString(password), row.Id);
                    publicChains.Remember(snapshot.CompanyId, material);
                    if (row.Id == snapshot.CertificateId)
                    {
                        if (material.Thumbprint != snapshot.Thumbprint || material.NotAfter != snapshot.NotAfter
                            || material.Certificate.NotBefore.ToUniversalTime() != snapshot.NotBefore || snapshot.HasPrivateKey != true
                            || material.SerialNumber != snapshot.SerialNumber)
                            throw new InvalidOperationException("FISCAL_CERTIFICATE_METADATA_CHANGED");
                        decision = evaluator.Evaluate(material.Certificate, material.AuxiliaryCertificates, snapshot.Ruc, DateTime.UtcNow);
                    }
                }
                catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException)
                {
                    if (row.Id == snapshot.CertificateId) throw new InvalidOperationException("FISCAL_PREFLIGHT_MATERIAL_UNAVAILABLE");
                    // A broken archived credential cannot hide the active tenant's result.
                }
                finally
                {
                    if (pfx is not null) CryptographicOperations.ZeroMemory(pfx);
                    if (password is not null) CryptographicOperations.ZeroMemory(password);
                }
            }
            var current = await FiscalTrustSnapshots.Query(db).SingleAsync(s => s.CompanyId == snapshot.CompanyId, ct);
            if (current != snapshot) { cache.Record(current, null, "FISCAL_PREFLIGHT_SNAPSHOT_CHANGED"); return; }
            cache.Record(snapshot, decision, decision?.Reason ?? "FISCAL_PREFLIGHT_NOT_OBSERVED");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { cache.Record(snapshot, null, "FISCAL_PREFLIGHT_EVALUATION_ERROR"); }
    }
}

public sealed class FiscalTrustMonitorWorker(IServiceScopeFactory scopes, IOptions<FiscalTrustOptions> options,
    ILogger<FiscalTrustMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FiscalTrustMonitor>().RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Fiscal trust monitor cycle unavailable; absent/stale results remain UNKNOWN."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.MonitorIntervalSeconds, 5, 300)), stoppingToken);
        }
    }
}
