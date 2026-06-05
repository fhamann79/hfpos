using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using QRCoder;

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

    public async Task<string> GetAuthorizedXmlAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var sale = await LoadSaleSnapshotAsync(saleId, operationalContext);

        ValidateSaleCanDownloadAuthorizedXml(sale);

        var responseXml = await GetLatestSuccessfulAuthorizationResponseXmlAsync(sale, operationalContext);

        if (string.IsNullOrWhiteSpace(responseXml))
        {
            throw new InvalidOperationException("SRI_AUTHORIZED_XML_NOT_FOUND");
        }

        var authorizationNode = ExtractAuthorizationNode(
            responseXml,
            invalidResponseErrorCode: "SRI_AUTHORIZED_XML_INVALID_RESPONSE",
            notFoundErrorCode: "SRI_AUTHORIZED_XML_NOT_FOUND");

        return authorizationNode.ToString(SaveOptions.DisableFormatting);
    }

    public async Task<SriRideDto> GetRideAsync(int saleId)
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var sale = await LoadSaleSnapshotAsync(saleId, operationalContext);

        ValidateSaleCanBuildRide(sale);

        var responseXml = await GetLatestSuccessfulAuthorizationResponseXmlAsync(sale, operationalContext);

        if (string.IsNullOrWhiteSpace(responseXml))
        {
            throw new InvalidOperationException("SRI_RIDE_NOT_FOUND");
        }

        var authorizationNode = ExtractAuthorizationNode(
            responseXml,
            invalidResponseErrorCode: "SRI_RIDE_INVALID_AUTHORIZED_XML",
            notFoundErrorCode: "SRI_RIDE_NOT_FOUND");

        var ride = BuildRide(sale, authorizationNode);
        await ApplyRideBrandingAsync(ride, sale.CompanyId);

        return ride;
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

    private static void ValidateSaleCanDownloadAuthorizedXml(Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SRI_AUTHORIZED_XML_ONLY_INVOICE");
        }

        var isAuthorized = sale.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(sale.SriAuthorizationStatus, "AUTORIZADO", StringComparison.OrdinalIgnoreCase);

        if (!isAuthorized)
        {
            throw new InvalidOperationException("SRI_AUTHORIZED_XML_SALE_NOT_AUTHORIZED");
        }

        if (string.IsNullOrWhiteSpace(sale.AuthorizationNumber))
        {
            throw new InvalidOperationException("SRI_AUTHORIZED_XML_NOT_FOUND");
        }
    }

    private static void ValidateSaleCanBuildRide(Sale sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            throw new InvalidOperationException("SRI_RIDE_ONLY_AUTHORIZED_INVOICE");
        }

        var isAuthorized = sale.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(sale.SriAuthorizationStatus, "AUTORIZADO", StringComparison.OrdinalIgnoreCase);

        if (!isAuthorized)
        {
            throw new InvalidOperationException("SRI_RIDE_ONLY_AUTHORIZED_INVOICE");
        }
    }

    private async Task<string?> GetLatestSuccessfulAuthorizationResponseXmlAsync(
        Sale sale,
        OperationalContext operationalContext)
        => await _context.SriSubmissionAttempts
            .AsNoTracking()
            .Where(a => a.SaleId == sale.Id
                && a.CompanyId == operationalContext.CompanyId
                && a.EstablishmentId == operationalContext.EstablishmentId
                && a.EmissionPointId == operationalContext.EmissionPointId
                && a.AttemptType == SriSubmissionAttemptType.Authorization
                && a.Status == SriSubmissionAttemptStatus.Success
                && a.AuthorizationStatus != null
                && a.AuthorizationStatus.ToUpper() == "AUTORIZADO"
                && a.ResponseXml != null
                && a.ResponseXml != string.Empty)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => a.ResponseXml)
            .FirstOrDefaultAsync();

    private static XElement ExtractAuthorizationNode(
        string responseXml,
        string invalidResponseErrorCode,
        string notFoundErrorCode)
    {
        var document = LoadXmlDocument(responseXml, invalidResponseErrorCode);
        var authorizationNode = FindElement(document, "autorizacion");

        return authorizationNode ?? throw new InvalidOperationException(notFoundErrorCode);
    }

    private static SriRideDto BuildRide(Sale sale, XElement authorizationNode)
    {
        var invoiceDocument = ExtractComprobanteDocument(authorizationNode);
        var invoice = invoiceDocument.Root
            ?? throw new InvalidOperationException("SRI_RIDE_INVALID_AUTHORIZED_XML");
        var infoTributaria = ChildElement(invoice, "infoTributaria")
            ?? throw new InvalidOperationException("SRI_RIDE_INVALID_AUTHORIZED_XML");
        var infoFactura = ChildElement(invoice, "infoFactura")
            ?? throw new InvalidOperationException("SRI_RIDE_INVALID_AUTHORIZED_XML");

        var environment = ChildValue(infoTributaria, "ambiente")
            ?? sale.SriEnvironment?.ToString(CultureInfo.InvariantCulture);
        var emissionType = ChildValue(infoTributaria, "tipoEmision")
            ?? sale.SriEmissionType?.ToString(CultureInfo.InvariantCulture);
        var accessKey = ChildValue(infoTributaria, "claveAcceso") ?? sale.AccessKey;

        return new SriRideDto
        {
            SaleId = sale.Id,
            DocumentTypeLabel = "Factura",
            DocumentNumber = BuildDocumentNumber(infoTributaria) ?? sale.Number,
            AccessKey = accessKey,
            Qr = BuildRideQr(accessKey),
            AuthorizationNumber = ChildValue(authorizationNode, "numeroAutorizacion") ?? sale.AuthorizationNumber,
            AuthorizationDate = ParseSriDate(ChildValue(authorizationNode, "fechaAutorizacion")) ?? sale.AuthorizedAt,
            EnvironmentLabel = SriEnvironmentLabel(environment),
            EmissionTypeLabel = SriEmissionTypeLabel(emissionType),
            IssueDate = ParseSriDate(ChildValue(infoFactura, "fechaEmision")) ?? sale.DocumentIssuedAt,
            Issuer = new SriRideIssuerDto
            {
                Ruc = ChildValue(infoTributaria, "ruc"),
                LegalName = ChildValue(infoTributaria, "razonSocial"),
                TradeName = ChildValue(infoTributaria, "nombreComercial"),
                MatrixAddress = ChildValue(infoTributaria, "dirMatriz"),
                EstablishmentAddress = ChildValue(infoFactura, "dirEstablecimiento"),
                AccountingRequired = ChildValue(infoFactura, "obligadoContabilidad"),
                TaxpayerRegime = ChildValue(infoFactura, "contribuyenteRimpe")
                    ?? ChildValue(infoFactura, "regimenMicroempresas")
            },
            Buyer = new SriRideBuyerDto
            {
                IdentificationType = BuyerIdentificationTypeLabel(ChildValue(infoFactura, "tipoIdentificacionComprador")),
                Identification = ChildValue(infoFactura, "identificacionComprador"),
                LegalName = ChildValue(infoFactura, "razonSocialComprador")
            },
            Items = BuildRideItems(invoice),
            Totals = BuildRideTotals(sale, infoFactura),
            Payments = BuildRidePayments(sale, infoFactura),
            AdditionalInfo = BuildRideAdditionalInfo(invoice)
        };
    }

    private static SriRideQrDto? BuildRideQr(string? accessKey)
    {
        var content = TrimToNull(accessKey);

        if (content is null)
        {
            return null;
        }

        try
        {
            using var qrCodeData = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new SvgQRCode(qrCodeData);
            var svg = qrCode.GetGraphic();
            var dataUrl = $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";

            return new SriRideQrDto
            {
                Content = content,
                DataUrl = dataUrl
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task ApplyRideBrandingAsync(SriRideDto ride, int companyId)
    {
        var branding = await _context.CompanyBrandings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId);

        if (branding is null)
        {
            return;
        }

        var logoConfigured = branding.LogoBytes is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(branding.LogoContentType);
        var logoBase64 = logoConfigured
            ? Convert.ToBase64String(branding.LogoBytes!)
            : null;

        ride.Branding = new SriRideBrandingDto
        {
            LogoConfigured = logoConfigured,
            LogoContentType = logoConfigured ? branding.LogoContentType : null,
            LogoDataUrl = logoConfigured ? $"data:{branding.LogoContentType};base64,{logoBase64}" : null,
            PrimaryColor = branding.PrimaryColor,
            DocumentFooterText = branding.DocumentFooterText
        };

        if (!string.IsNullOrWhiteSpace(branding.DocumentFooterText))
        {
            ride.FooterNote = branding.DocumentFooterText;
        }
    }

    private static XDocument ExtractComprobanteDocument(XElement authorizationNode)
    {
        var comprobanteNode = FindElement(authorizationNode, "comprobante")
            ?? throw new InvalidOperationException("SRI_RIDE_INVALID_AUTHORIZED_XML");
        var embeddedInvoice = comprobanteNode.Elements().FirstOrDefault();

        if (embeddedInvoice is not null)
        {
            return new XDocument(new XElement(embeddedInvoice));
        }

        var comprobanteXml = TrimToNull(comprobanteNode.Value);

        if (comprobanteXml is null)
        {
            throw new InvalidOperationException("SRI_RIDE_INVALID_AUTHORIZED_XML");
        }

        return LoadXmlDocument(comprobanteXml, "SRI_RIDE_INVALID_AUTHORIZED_XML");
    }

    private static XDocument LoadXmlDocument(string xml, string errorCode)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            return XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(errorCode, ex);
        }
    }

    private static List<SriRideItemDto> BuildRideItems(XElement invoice)
    {
        var detalles = ChildElement(invoice, "detalles");

        return ChildElements(detalles, "detalle")
            .Select(detail =>
            {
                var impuestos = ChildElement(detail, "impuestos");
                var taxAmount = ChildElements(impuestos, "impuesto")
                    .Select(tax => ParseDecimal(ChildValue(tax, "valor")) ?? 0)
                    .Sum();
                var subtotal = ParseDecimal(ChildValue(detail, "precioTotalSinImpuesto")) ?? 0;

                return new SriRideItemDto
                {
                    MainCode = ChildValue(detail, "codigoPrincipal"),
                    Description = ChildValue(detail, "descripcion"),
                    Quantity = ParseDecimal(ChildValue(detail, "cantidad")) ?? 0,
                    UnitPrice = ParseDecimal(ChildValue(detail, "precioUnitario")) ?? 0,
                    Discount = ParseDecimal(ChildValue(detail, "descuento")) ?? 0,
                    Subtotal = subtotal,
                    TaxAmount = taxAmount,
                    LineTotal = subtotal + taxAmount
                };
            })
            .ToList();
    }

    private static SriRideTotalsDto BuildRideTotals(Sale sale, XElement infoFactura)
    {
        var totals = new SriRideTotalsDto
        {
            SubtotalWithoutTaxes = ParseDecimal(ChildValue(infoFactura, "totalSinImpuestos")) ?? sale.Subtotal,
            TotalDiscount = ParseDecimal(ChildValue(infoFactura, "totalDescuento")) ?? sale.DiscountAmount,
            TaxAmount = sale.TaxAmount,
            Total = ParseDecimal(ChildValue(infoFactura, "importeTotal")) ?? sale.Total,
            Currency = ChildValue(infoFactura, "moneda") ?? "USD"
        };

        var taxSummary = ChildElement(infoFactura, "totalConImpuestos");
        var hasXmlTaxTotals = false;
        var xmlTaxAmount = 0m;

        foreach (var tax in ChildElements(taxSummary, "totalImpuesto"))
        {
            hasXmlTaxTotals = true;
            var code = ChildValue(tax, "codigo");
            var percentageCode = ChildValue(tax, "codigoPorcentaje");
            var taxableBase = ParseDecimal(ChildValue(tax, "baseImponible")) ?? 0;
            var amount = ParseDecimal(ChildValue(tax, "valor")) ?? 0;

            xmlTaxAmount += amount;

            if (code != "2")
            {
                continue;
            }

            switch (percentageCode)
            {
                case "4":
                    totals.Vat15Subtotal += taxableBase;
                    break;
                case "5":
                    totals.Vat5Subtotal += taxableBase;
                    break;
                case "0":
                    totals.Vat0Subtotal += taxableBase;
                    break;
                case "6":
                    totals.NotSubjectSubtotal += taxableBase;
                    break;
                case "7":
                    totals.ExemptSubtotal += taxableBase;
                    break;
            }
        }

        if (hasXmlTaxTotals)
        {
            totals.TaxAmount = xmlTaxAmount;
        }
        else
        {
            totals.Vat15Subtotal = sale.Vat15Subtotal;
            totals.Vat5Subtotal = sale.Vat5Subtotal;
            totals.Vat0Subtotal = sale.Vat0Subtotal;
            totals.ExemptSubtotal = sale.VatExemptSubtotal;
            totals.NotSubjectSubtotal = sale.VatNotSubjectSubtotal;
        }

        return totals;
    }

    private static List<SriRidePaymentDto> BuildRidePayments(Sale sale, XElement infoFactura)
    {
        var pagos = ChildElement(infoFactura, "pagos");
        var payments = ChildElements(pagos, "pago")
            .Select(payment => new SriRidePaymentDto
            {
                PaymentMethod = SriPaymentMethodLabel(ChildValue(payment, "formaPago")),
                Amount = ParseDecimal(ChildValue(payment, "total")) ?? 0
            })
            .Where(payment => !string.IsNullOrWhiteSpace(payment.PaymentMethod) || payment.Amount > 0)
            .ToList();

        if (payments.Count > 0)
        {
            return payments;
        }

        return new List<SriRidePaymentDto>
        {
            new()
            {
                PaymentMethod = SalePaymentMethodLabel(sale.PaymentMethod),
                Amount = sale.Total
            }
        };
    }

    private static List<SriRideAdditionalInfoDto> BuildRideAdditionalInfo(XElement invoice)
    {
        var infoAdicional = ChildElement(invoice, "infoAdicional");
        var additionalInfo = new List<SriRideAdditionalInfoDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in ChildElements(infoAdicional, "campoAdicional"))
        {
            var value = TrimToNull(field.Value);

            if (value is null)
            {
                continue;
            }

            var name = TrimToNull(field.Attributes()
                .FirstOrDefault(attribute => string.Equals(
                    attribute.Name.LocalName,
                    "nombre",
                    StringComparison.OrdinalIgnoreCase))
                ?.Value) ?? "Informacion adicional";
            var key = $"{name}\u001F{value}";

            if (!seen.Add(key))
            {
                continue;
            }

            additionalInfo.Add(new SriRideAdditionalInfoDto
            {
                Name = name,
                Value = value
            });
        }

        return additionalInfo;
    }

    private static XElement? FindElement(XContainer container, string localName)
    {
        if (container is XDocument { Root: not null } document
            && HasLocalName(document.Root, localName))
        {
            return document.Root;
        }

        if (container is XElement element
            && HasLocalName(element, localName))
        {
            return element;
        }

        return container.Descendants()
            .FirstOrDefault(element => HasLocalName(element, localName));
    }

    private static XElement? ChildElement(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(element => HasLocalName(element, localName));

    private static IEnumerable<XElement> ChildElements(XElement? parent, string localName)
        => parent?.Elements().Where(element => HasLocalName(element, localName)) ?? Enumerable.Empty<XElement>();

    private static string? ChildValue(XElement parent, string localName)
        => TrimToNull(ChildElement(parent, localName)?.Value);

    private static bool HasLocalName(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string? BuildDocumentNumber(XElement infoTributaria)
    {
        var establishment = ChildValue(infoTributaria, "estab");
        var emissionPoint = ChildValue(infoTributaria, "ptoEmi");
        var sequential = ChildValue(infoTributaria, "secuencial");

        return establishment is not null && emissionPoint is not null && sequential is not null
            ? $"{establishment}-{emissionPoint}-{sequential}"
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        var normalized = TrimToNull(value);

        if (normalized is null)
        {
            return null;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("es-EC"), out var localValue)
            ? localValue
            : null;
    }

    private static DateTime? ParseSriDate(string? value)
    {
        var normalized = TrimToNull(value);

        if (normalized is null)
        {
            return null;
        }

        string[] formats =
        [
            "dd/MM/yyyy",
            "d/M/yyyy",
            "dd-MM-yyyy",
            "d-M-yyyy",
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fffK",
            "yyyy-MM-ddTHH:mm:ss.fffzzz"
        ];

        if (DateTime.TryParseExact(
            normalized,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var exactValue))
        {
            return exactValue;
        }

        if (DateTime.TryParse(
            normalized,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var invariantValue))
        {
            return invariantValue;
        }

        return DateTime.TryParse(
            normalized,
            CultureInfo.GetCultureInfo("es-EC"),
            DateTimeStyles.AllowWhiteSpaces,
            out var localValue)
                ? localValue
                : null;
    }

    private static string? SriEnvironmentLabel(string? environment)
        => environment switch
        {
            "1" => "Pruebas",
            "2" => "Produccion",
            _ => environment
        };

    private static string? SriEmissionTypeLabel(string? emissionType)
        => emissionType switch
        {
            "1" => "Normal",
            _ => emissionType
        };

    private static string? BuyerIdentificationTypeLabel(string? code)
        => code switch
        {
            "04" => "RUC",
            "05" => "Cedula",
            "06" => "Pasaporte",
            "07" => "Consumidor final",
            "08" => "Identificacion exterior",
            "09" => "Placa",
            _ => code
        };

    private static string? SriPaymentMethodLabel(string? code)
        => code switch
        {
            "01" => "Sin uso del sistema financiero",
            "15" => "Compensacion de deudas",
            "16" => "Tarjeta de debito",
            "17" => "Dinero electronico",
            "18" => "Tarjeta prepago",
            "19" => "Tarjeta de credito",
            "20" => "Otros",
            "21" => "Endoso de titulos",
            _ => code
        };

    private static string SalePaymentMethodLabel(SalePaymentMethod paymentMethod)
        => paymentMethod switch
        {
            SalePaymentMethod.Cash => "Efectivo",
            SalePaymentMethod.Card => "Tarjeta",
            SalePaymentMethod.Transfer => "Transferencia",
            _ => "Otro"
        };

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
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
