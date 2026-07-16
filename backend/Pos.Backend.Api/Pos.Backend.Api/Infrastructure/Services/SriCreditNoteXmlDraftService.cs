using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriCreditNoteXmlDraftService : ISriCreditNoteXmlDraftService
{
    private const string DocumentCode = "04";
    private const string ModifiedDocumentCode = "01";
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex OriginalDocumentNumberRegex =
        new(@"^\d{3}-\d{3}-\d{9}$", RegexOptions.Compiled);

    public string GenerateCreditNoteXmlDraft(SriCreditNoteXmlDraftRequest request)
    {
        try
        {
            ValidateContext(request);

            var creditNote = request.CreditNote;
            var company = request.Company;

            var infoTributaria = new XElement("infoTributaria",
                new XElement("ambiente", request.Environment.ToString(CultureInfo.InvariantCulture)),
                new XElement("tipoEmision", request.EmissionType.ToString(CultureInfo.InvariantCulture)),
                new XElement(
                    "razonSocial",
                    RequiredText(
                        company.Name,
                        300,
                        "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")),
                OptionalElement("nombreComercial", company.TradeName, 300),
                new XElement("ruc", RequiredNumeric(company.Ruc, 13, "INVALID_ISSUER_RUC")),
                new XElement(
                    "claveAcceso",
                    RequiredNumeric(
                        creditNote.AccessKey,
                        49,
                        "INVALID_SRI_DOCUMENT_CONTEXT")),
                new XElement("codDoc", DocumentCode),
                new XElement(
                    "estab",
                    RequiredNumeric(
                        creditNote.EstablishmentCodeSnapshot,
                        3,
                        "INVALID_SRI_DOCUMENT_CONTEXT")),
                new XElement(
                    "ptoEmi",
                    RequiredNumeric(
                        creditNote.EmissionPointCodeSnapshot,
                        3,
                        "INVALID_SRI_DOCUMENT_CONTEXT")),
                new XElement(
                    "secuencial",
                    creditNote.Sequential!.Value.ToString(
                        "000000000",
                        CultureInfo.InvariantCulture)),
                new XElement(
                    "dirMatriz",
                    RequiredText(
                        company.MatrixAddress,
                        300,
                        "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")),
                ResolveRimpeElement(company.TaxpayerRegime));

            var infoNotaCredito = new XElement("infoNotaCredito",
                new XElement(
                    "fechaEmision",
                    request.FiscalEmissionDate.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture)),
                OptionalElement("dirEstablecimiento", request.Establishment.Address, 300),
                new XElement(
                    "tipoIdentificacionComprador",
                    RequiredText(
                        creditNote.BuyerIdentificationTypeSnapshot,
                        2,
                        "SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED")),
                new XElement(
                    "razonSocialComprador",
                    RequiredText(
                        creditNote.BuyerNameSnapshot,
                        300,
                        "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")),
                new XElement(
                    "identificacionComprador",
                    RequiredText(
                        creditNote.BuyerIdentificationSnapshot,
                        20,
                        "INVALID_SRI_CUSTOMER_IDENTIFICATION")),
                OptionalElement(
                    "contribuyenteEspecial",
                    NormalizeSpecialTaxpayerNumber(company.SpecialTaxpayerNumber),
                    13),
                new XElement(
                    "obligadoContabilidad",
                    company.IsAccountingRequired ? "SI" : "NO"),
                new XElement("codDocModificado", ModifiedDocumentCode),
                new XElement(
                    "numDocModificado",
                    RequiredOriginalDocumentNumber(creditNote.OriginalSaleNumberSnapshot)),
                new XElement(
                    "fechaEmisionDocSustento",
                    request.OriginalInvoiceFiscalEmissionDate.ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture)),
                new XElement("totalSinImpuestos", FormatMoney(creditNote.Subtotal)),
                new XElement("valorModificacion", FormatMoney(creditNote.Total)),
                new XElement("moneda", "DOLAR"),
                BuildTaxTotals(creditNote),
                new XElement(
                    "motivo",
                    RequiredText(
                        creditNote.Reason,
                        300,
                        "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")));

            var document = new XElement("notaCredito",
                new XAttribute("id", "comprobante"),
                new XAttribute("version", "1.1.0"),
                infoTributaria,
                infoNotaCredito,
                new XElement("detalles",
                    creditNote.Items
                        .OrderBy(item => item.Id)
                        .Select(BuildDetail)));

            return new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    document)
                .ToString(SaveOptions.DisableFormatting);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED",
                ex);
        }
    }

    private static void ValidateContext(SriCreditNoteXmlDraftRequest request)
    {
        if (request.CreditNote is null
            || request.Company is null
            || request.Establishment is null)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        var creditNote = request.CreditNote;

        if (request.Environment is not (1 or 2)
            || request.EmissionType != 1
            || creditNote.Sequential is null
            || creditNote.Sequential <= 0
            || creditNote.DocumentIssuedAt is null
            || string.IsNullOrWhiteSpace(creditNote.Number))
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        RequiredNumeric(creditNote.AccessKey, 49, "INVALID_SRI_DOCUMENT_CONTEXT");
        RequiredNumeric(
            creditNote.EstablishmentCodeSnapshot,
            3,
            "INVALID_SRI_DOCUMENT_CONTEXT");
        RequiredNumeric(
            creditNote.EmissionPointCodeSnapshot,
            3,
            "INVALID_SRI_DOCUMENT_CONTEXT");
        RequiredNumeric(request.Company.Ruc, 13, "INVALID_ISSUER_RUC");
        RequiredText(
            request.Company.MatrixAddress,
            300,
            "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING");

        var buyerIdentificationType = RequiredText(
            creditNote.BuyerIdentificationTypeSnapshot,
            2,
            "SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED");
        var buyerIdentification = RequiredText(
            creditNote.BuyerIdentificationSnapshot,
            20,
            "INVALID_SRI_CUSTOMER_IDENTIFICATION");
        ValidateBuyerIdentification(buyerIdentificationType, buyerIdentification);

        RequiredText(
            creditNote.BuyerNameSnapshot,
            300,
            "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING");
        RequiredText(
            creditNote.Reason,
            300,
            "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING");

        ValidateOriginalDocumentSnapshots(creditNote);

        if (creditNote.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING");
        }
    }

    private static void ValidateOriginalDocumentSnapshots(CreditNote creditNote)
    {
        RequiredOriginalDocumentNumber(creditNote.OriginalSaleNumberSnapshot);

        if (!IsNumeric(creditNote.OriginalSaleAccessKeySnapshot, 49)
            || string.IsNullOrWhiteSpace(
                NormalizeText(creditNote.OriginalSaleAuthorizationNumberSnapshot))
            || !creditNote.OriginalSaleAuthorizedAtSnapshot.HasValue
            || !creditNote.OriginalSaleDocumentIssuedAtSnapshot.HasValue)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_ORIGINAL_DOCUMENT_REQUIRED");
        }
    }

    private static void ValidateBuyerIdentification(
        string identificationType,
        string identification)
    {
        var isValid = identificationType switch
        {
            "04" => identification.Length == 13 && identification.All(char.IsDigit),
            "05" => identification.Length == 10 && identification.All(char.IsDigit),
            "06" => identification.Length <= 20 && identification.All(char.IsLetterOrDigit),
            "07" => identification == "9999999999999",
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException(
                identificationType is "04" or "05" or "06" or "07"
                    ? "INVALID_SRI_CUSTOMER_IDENTIFICATION"
                    : "SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED");
        }
    }

    private static XElement BuildTaxTotals(CreditNote creditNote)
    {
        var totals = creditNote.Items
            .GroupBy(item => item.VatCategory)
            .Select(group =>
            {
                var mapping = ResolveVatMapping(group.Key);
                return new
                {
                    mapping.PercentageCode,
                    Base = group.Sum(item => item.TaxableSubtotal),
                    Tax = group.Sum(item => item.TaxAmount)
                };
            })
            .Where(group => group.Base > 0m || group.Tax > 0m)
            .OrderBy(group => TaxOrder(group.PercentageCode))
            .Select(group => new XElement("totalImpuesto",
                new XElement("codigo", "2"),
                new XElement("codigoPorcentaje", group.PercentageCode),
                new XElement("baseImponible", FormatMoney(group.Base)),
                new XElement("valor", FormatMoney(group.Tax))))
            .ToList();

        if (totals.Count == 0)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING");
        }

        return new XElement("totalConImpuestos", totals);
    }

    private static XElement BuildDetail(CreditNoteItem item)
    {
        var vatMapping = ResolveVatMapping(item.VatCategory);

        return new XElement("detalle",
            new XElement(
                "codigoInterno",
                RequiredText(
                    item.ProductMainCodeSnapshot,
                    25,
                    "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")),
            OptionalElement(
                "codigoAdicional",
                item.ProductAuxiliaryCodeSnapshot,
                25),
            new XElement(
                "descripcion",
                RequiredText(
                    item.ProductNameSnapshot,
                    300,
                    "SRI_CREDIT_NOTE_XML_REQUIRED_FIELD_MISSING")),
            new XElement("cantidad", FormatQuantity(item.Quantity)),
            new XElement("precioUnitario", FormatUnitPrice(item.UnitPrice)),
            new XElement("descuento", FormatMoney(item.DiscountAmount)),
            new XElement(
                "precioTotalSinImpuesto",
                FormatMoney(item.TaxableSubtotal)),
            new XElement("impuestos",
                new XElement("impuesto",
                    new XElement("codigo", "2"),
                    new XElement("codigoPorcentaje", vatMapping.PercentageCode),
                    new XElement("tarifa", FormatTaxRate(vatMapping.Rate)),
                    new XElement(
                        "baseImponible",
                        FormatMoney(item.TaxableSubtotal)),
                    new XElement("valor", FormatMoney(item.TaxAmount)))));
    }

    private static (string PercentageCode, decimal Rate) ResolveVatMapping(
        ProductVatCategory category)
    {
        return category switch
        {
            ProductVatCategory.Vat15 => ("4", 15m),
            ProductVatCategory.Vat5 => ("5", 5m),
            ProductVatCategory.Vat0 => ("0", 0m),
            ProductVatCategory.VatNotSubject => ("6", 0m),
            ProductVatCategory.VatExempt => ("7", 0m),
            _ => throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED")
        };
    }

    private static XElement? OptionalElement(
        string name,
        string? value,
        int maxLength)
    {
        var normalized = OptionalText(value, maxLength);
        return normalized is null ? null : new XElement(name, normalized);
    }

    private static XElement? ResolveRimpeElement(string? taxpayerRegime)
    {
        var normalized = NormalizeText(taxpayerRegime);
        if (normalized is null
            || !normalized.Contains("RIMPE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new XElement(
            "contribuyenteRimpe",
            "CONTRIBUYENTE R\u00c9GIMEN RIMPE");
    }

    private static string? NormalizeSpecialTaxpayerNumber(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length is < 3 or > 13 || !normalized.All(char.IsDigit))
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        return normalized;
    }

    private static string RequiredOriginalDocumentNumber(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null || !OriginalDocumentNumberRegex.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_ORIGINAL_DOCUMENT_REQUIRED");
        }

        return normalized;
    }

    private static string RequiredNumeric(
        string? value,
        int expectedLength,
        string errorCode)
    {
        var normalized = RequiredText(value, expectedLength, errorCode);

        if (normalized.Length != expectedLength || !normalized.All(char.IsDigit))
        {
            throw new InvalidOperationException(errorCode);
        }

        return normalized;
    }

    private static string RequiredText(
        string? value,
        int maxLength,
        string errorCode)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            throw new InvalidOperationException(errorCode);
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        return normalized;
    }

    private static string? OptionalText(string? value, int maxLength)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex.Replace(value.Trim(), " ");
        return normalized.Length == 0 ? null : normalized;
    }

    private static string FormatMoney(decimal value)
    {
        if (value < 0m)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        if (value <= 0m)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        return FormatFlexibleDecimal(value);
    }

    private static string FormatUnitPrice(decimal value)
    {
        if (value < 0m)
        {
            throw new InvalidOperationException(
                "SRI_CREDIT_NOTE_XML_DRAFT_GENERATION_FAILED");
        }

        return FormatFlexibleDecimal(value);
    }

    private static string FormatFlexibleDecimal(decimal value)
    {
        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatTaxRate(decimal rate)
        => rate.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsNumeric(string? value, int expectedLength)
    {
        return value is not null
            && value.Length == expectedLength
            && value.All(char.IsDigit);
    }

    private static int TaxOrder(string percentageCode)
        => percentageCode switch
        {
            "4" => 0,
            "5" => 1,
            "0" => 2,
            "6" => 3,
            "7" => 4,
            _ => 99
        };
}
