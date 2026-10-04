using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriInvoiceSigningService : ISriInvoiceSigningService
{
    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriSigningCertificateProvider _certificateProvider;
    private readonly ISriXadesBesSigner _sriXadesBesSigner;
    private readonly ISriInvoiceXmlValidator _sriInvoiceXmlValidator;
    private readonly ISalesService _salesService;
    private readonly ILogger<SriInvoiceSigningService> _logger;
    private readonly ElectronicIssuingCoordinator _coordinator;

    public SriInvoiceSigningService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriSigningCertificateProvider certificateProvider,
        ISriXadesBesSigner sriXadesBesSigner,
        ISriInvoiceXmlValidator sriInvoiceXmlValidator,
        ISalesService salesService,
        ILogger<SriInvoiceSigningService> logger,
        ElectronicIssuingCoordinator coordinator)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _certificateProvider = certificateProvider;
        _sriXadesBesSigner = sriXadesBesSigner;
        _sriInvoiceXmlValidator = sriInvoiceXmlValidator;
        _salesService = salesService;
        _logger = logger;
        _coordinator = coordinator;
    }

    public async Task<SaleDto> SignInvoiceDraftAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        await SignCoreAsync(saleId, operationalContext, null, CancellationToken.None);
        return await _salesService.GetByIdAsync(saleId) ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
    }

    internal Task SignClaimAsync(Core.Entities.ElectronicIssuingJob claim, CancellationToken ct)
        => SignCoreAsync(claim.SaleId, null, claim, ct);

    private async Task SignCoreAsync(int saleId, OperationalContext? manual, Core.Entities.ElectronicIssuingJob? claim, CancellationToken ct)
    {
        var admission = await _coordinator.AdmitAsync(saleId, ElectronicIssuingPhase.ReadyToSign, manual, claim, ct);
        try
        {
            await _coordinator.CompleteAsync(admission, async (sale, job) =>
            {
            ValidateSaleCanBeSigned(sale);
            _sriInvoiceXmlValidator.ValidateUnsignedInvoiceXml(sale.SriXmlDraft!);
            using var certificateMaterial = _certificateProvider is SriSigningCertificateProvider provider
                ? await provider.GetForAdmissionAsync(admission)
                : throw new InvalidOperationException("FISCAL_CERTIFICATE_PROVIDER_REQUIRED");
            var now = DateTime.UtcNow;
            var signedXml = _sriXadesBesSigner.SignInvoiceXml(
                sale.SriXmlDraft!,
                certificateMaterial.Certificate,
                sale.AccessKey!,
                now);

            sale.SriSignedXml = signedXml;
            sale.SriSignedAt = now;
            sale.SriSignatureHash = ComputeSha256Hash(signedXml);
            sale.SriSigningCertificateThumbprint = certificateMaterial.Thumbprint;
            sale.SriSigningCertificateSubject = certificateMaterial.Subject;
            sale.SriSigningCertificateSerialNumber = certificateMaterial.SerialNumber;
            sale.UpdatedAt = now;

            ElectronicIssuingCoordinator.Schedule(job, ElectronicIssuingJobState.Queued, ElectronicIssuingPhase.ReadyToSubmit);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex.Message != "FISCAL_LEASE_LOST")
        {
            await _coordinator.FailAsync(admission, ex is InvalidOperationException or KeyNotFoundException
                ? ex.Message : "SRI_XML_SIGNING_FAILED", false, ct);
            throw;
        }
    }

    public async Task<string> GetSignedXmlAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

        var sale = await _context.Sales
            .AsNoTracking()
            .Where(s => s.Id == saleId
                && s.CompanyId == operationalContext.CompanyId
                && s.EstablishmentId == operationalContext.EstablishmentId
                && s.EmissionPointId == operationalContext.EmissionPointId)
            .Select(s => new
            {
                s.SriSignedXml
            })
            .FirstOrDefaultAsync();

        if (sale is null)
        {
            throw new KeyNotFoundException("SALE_NOT_FOUND");
        }

        if (string.IsNullOrWhiteSpace(sale.SriSignedXml))
        {
            throw new KeyNotFoundException("SRI_SIGNED_XML_NOT_FOUND");
        }

        return sale.SriSignedXml;
    }

    private static void ValidateSaleCanBeSigned(Core.Entities.Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SRI_SIGNING_ONLY_INVOICE");
        }

        if (sale.Status == SaleStatus.Voided)
        {
            throw new InvalidOperationException("SRI_SIGNING_SALE_VOIDED");
        }

        if (string.IsNullOrWhiteSpace(sale.AccessKey))
        {
            throw new InvalidOperationException("SRI_ACCESS_KEY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(sale.SriXmlDraft))
        {
            throw new KeyNotFoundException("SRI_XML_DRAFT_NOT_FOUND");
        }

        if (!string.IsNullOrWhiteSpace(sale.SriSignedXml))
        {
            throw new InvalidOperationException("SRI_XML_ALREADY_SIGNED");
        }
    }

    private static string ComputeSha256Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
