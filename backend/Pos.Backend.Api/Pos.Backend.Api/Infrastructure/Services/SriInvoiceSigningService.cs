using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
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
    private readonly ISalesService _salesService;
    private readonly ILogger<SriInvoiceSigningService> _logger;

    public SriInvoiceSigningService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriSigningCertificateProvider certificateProvider,
        ISalesService salesService,
        ILogger<SriInvoiceSigningService> logger)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _certificateProvider = certificateProvider;
        _salesService = salesService;
        _logger = logger;
    }

    public async Task<SaleDto> SignInvoiceDraftAsync(int saleId)
    {
        OperationalContext? operationalContext = null;

        try
        {
            operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();

            await using var transaction = await _context.Database.BeginTransactionAsync();

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

            if (sale is null)
            {
                throw new KeyNotFoundException("SALE_NOT_FOUND");
            }

            ValidateSaleCanBeSigned(sale);

            using var certificateMaterial = await _certificateProvider.GetActiveCertificateMaterialAsync();
            var signedXml = SignXml(sale.SriXmlDraft!, certificateMaterial.Certificate);
            var now = DateTime.UtcNow;

            sale.SriSignedXml = signedXml;
            sale.SriSignedAt = now;
            sale.SriSignatureHash = ComputeSha256Hash(signedXml);
            sale.SriSigningCertificateThumbprint = certificateMaterial.Thumbprint;
            sale.SriSigningCertificateSubject = certificateMaterial.Subject;
            sale.SriSigningCertificateSerialNumber = certificateMaterial.SerialNumber;
            sale.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "SRI XML draft signed. SaleId {SaleId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId} CertificateId {CertificateId}",
                sale.Id,
                operationalContext.CompanyId,
                operationalContext.EstablishmentId,
                operationalContext.EmissionPointId,
                certificateMaterial.CertificateId);

            return await _salesService.GetByIdAsync(sale.Id)
                ?? throw new KeyNotFoundException("SALE_NOT_FOUND");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "SRI XML signing failed. SaleId {SaleId} ErrorCode {ErrorCode} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                saleId,
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
                "Unexpected SRI XML signing failure. SaleId {SaleId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
                saleId,
                operationalContext?.CompanyId,
                operationalContext?.EstablishmentId,
                operationalContext?.EmissionPointId);
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED", ex);
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

    private static string SignXml(string unsignedXml, X509Certificate2 certificate)
    {
        try
        {
            var xmlDocument = LoadXmlDocument(unsignedXml);
            var root = xmlDocument.DocumentElement;

            if (root is null || !string.Equals(root.Name, "factura", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
            }

            using var privateKey = certificate.GetRSAPrivateKey();

            if (privateKey is null)
            {
                throw new InvalidOperationException("CERTIFICATE_WITHOUT_PRIVATE_KEY");
            }

            var signedXml = new SignedXml(xmlDocument)
            {
                SigningKey = privateKey
            };

            var signedInfo = signedXml.SignedInfo
                ?? throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");

            signedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

            var reference = new Reference
            {
                Uri = "",
                DigestMethod = SignedXml.XmlDsigSHA256Url
            };

            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();

            var signatureElement = signedXml.GetXml();
            root.AppendChild(xmlDocument.ImportNode(signatureElement, true));

            VerifySignature(xmlDocument, certificate);

            return xmlDocument.OuterXml;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED", ex);
        }
    }

    private static XmlDocument LoadXmlDocument(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        document.Load(xmlReader);

        return document;
    }

    private static void VerifySignature(
        XmlDocument xmlDocument,
        X509Certificate2 certificate)
    {
        var signatureNode = xmlDocument
            .GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .FirstOrDefault();

        if (signatureNode is null)
        {
            throw new InvalidOperationException("SRI_XML_SIGNING_FAILED");
        }

        var signedXml = new SignedXml(xmlDocument);
        signedXml.LoadXml(signatureNode);

        if (!signedXml.CheckSignature(certificate, true))
        {
            throw new InvalidOperationException("SRI_SIGNATURE_VALIDATION_FAILED");
        }
    }

    private static string ComputeSha256Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
