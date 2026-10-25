using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class FiscalExecutionGuard(PosDbContext db, ISriSigningCertificateProvider provider,
    IFiscalCertificateEvaluator evaluator, FiscalPublicChainCache publicChains)
{
    internal async Task BeforeCreateProductionAsync(OperationalContext authority)
    {
        using var material = provider is SriSigningCertificateProvider concrete
            ? await concrete.GetForCreationAsync(authority) : throw new InvalidOperationException("FISCAL_CERTIFICATE_PROVIDER_REQUIRED");
        var ruc = await db.Companies.AsNoTracking().Where(c => c.Id == authority.CompanyId).Select(c => c.Ruc).SingleAsync();
        RequireMaterial(material, ruc, 2);
    }

    internal void RequireMaterial(SriSigningCertificateMaterial material, string ruc, int environment)
    {
        if (environment == 1) return; // Sandbox never establishes production trust; see RC6 profile/runbook.
        if (environment != 2) throw new InvalidOperationException("INVALID_SRI_ENVIRONMENT");
        if (!material.Certificate.HasPrivateKey) throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
        Require(evaluator.Evaluate(material.Certificate, material.AuxiliaryCertificates, ruc, DateTime.UtcNow));
    }

    internal Task BeforeReceptionAsync(Sale sale, CancellationToken ct)
    {
        if (sale.SriEnvironment == 1) return Task.CompletedTask;
        var issuer = FiscalSnapshots.Read(sale.FiscalIssuerSnapshotJson)
            ?? throw new InvalidOperationException("FISCAL_LEGACY_REVIEW_REQUIRED");
        using var cert = FiscalSignedXmlVerifier.Verify(sale.SriSignedXml ?? "", sale.SriSignatureHash ?? "",
            sale.SriSigningCertificateThumbprint ?? "", sale.AccessKey ?? "", issuer.Ruc, "factura");
        var auxiliary = publicChains.Read(sale.CompanyId, cert.Thumbprint);
        try { Require(evaluator.Evaluate(cert, auxiliary, issuer.Ruc, DateTime.UtcNow)); }
        finally { foreach (var item in auxiliary) item.Dispose(); }
        return Task.CompletedTask;
    }

    internal void BeforeCreditNoteReception(CreditNote note, int environment)
    {
        if (environment == 1) return;
        var issuer = FiscalSnapshots.Read(note.FiscalIssuerSnapshotJson)
            ?? throw new InvalidOperationException("FISCAL_LEGACY_REVIEW_REQUIRED");
        using var cert = FiscalSignedXmlVerifier.Verify(note.SriSignedXml ?? "", note.SriSignatureHash ?? "",
            note.SriSigningCertificateThumbprint ?? "", note.AccessKey ?? "", issuer.Ruc, "notaCredito");
        var auxiliary = publicChains.Read(note.CompanyId, cert.Thumbprint);
        try { Require(evaluator.Evaluate(cert, auxiliary, issuer.Ruc, DateTime.UtcNow)); }
        finally { foreach (var item in auxiliary) item.Dispose(); }
    }

    internal void RememberPublicChain(int company, SriSigningCertificateMaterial material) => publicChains.Remember(company, material);

    private static void Require(FiscalTrustDecision decision)
    {
        if (!decision.Eligible) throw new InvalidOperationException(decision.Reason);
    }
}
