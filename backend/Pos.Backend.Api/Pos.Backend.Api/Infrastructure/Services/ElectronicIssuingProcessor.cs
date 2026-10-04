using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ElectronicIssuingProcessor(
    ElectronicIssuingCoordinator coordinator,
    ISriInvoiceSigningService signing,
    ISriSubmissionService submission)
{
    public async Task ProcessAsync(ElectronicIssuingJob claim, CancellationToken ct)
    {
        try
        {
            if (signing is not SriInvoiceSigningService signer || submission is not SriSubmissionService transport)
                throw new InvalidOperationException("FISCAL_EXECUTION_PROVIDER_REQUIRED");
            switch (claim.Phase)
            {
                case ElectronicIssuingPhase.ReadyToSign:
                    await signer.SignClaimAsync(claim, ct);
                    break;
                case ElectronicIssuingPhase.ReadyToSubmit:
                    await transport.SubmitClaimAsync(claim, ct);
                    break;
                default:
                    await transport.CheckClaimAsync(claim, ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (ex.Message == "FISCAL_LEASE_LOST") return;
            try
            {
                await coordinator.FailClaimAsync(claim,
                    ex is InvalidOperationException or KeyNotFoundException ? ex.Message : "FISCAL_TRANSIENT_FAILURE", ct);
            }
            catch (InvalidOperationException fenced) when (fenced.Message == "FISCAL_LEASE_LOST") { }
        }
    }
}

public sealed class ElectronicIssuingWorker(IServiceScopeFactory scopes, ILogger<ElectronicIssuingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<ElectronicIssuingJob> claims;
                await using (var scope = scopes.CreateAsyncScope())
                    claims = await scope.ServiceProvider.GetRequiredService<ElectronicIssuingCoordinator>()
                        .ClaimBatchAsync(1, stoppingToken); // No waiting job spends its lease behind an external call.
                foreach (var claim in claims)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ElectronicIssuingProcessor>().ProcessAsync(claim, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not log database/transport exception payloads or connection strings.
                logger.LogWarning("Electronic issuing iteration failed; durable leases will recover.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
