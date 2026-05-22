using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriXmlDraftService : ISriXmlDraftService
{
    private const int MaxProductCodeLength = 25;
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string GenerateInvoiceXmlDraft(SriXmlDraftRequest request)
    {
        var sale = request.Sale;

        ValidateInvoiceContext(sale, request.Environment, request.EmissionType);

        var buyer = ResolveBuyer(request.Customer);
        var matrixAddress = RequiredText(
            request.Company.MatrixAddress,
            300,
            "SRI_COMPANY_MATRIX_ADDRESS_REQUIRED");

        var infoTributaria = new XElement("infoTributaria",
            new XElement("ambiente", request.Environment.ToString(CultureInfo.InvariantCulture)),
            new XElement("tipoEmision", request.EmissionType.ToString(CultureInfo.InvariantCulture)),
            new XElement("razonSocial", RequiredText(request.Company.Name, 300, "SRI_XML_REQUIRED_FIELD_MISSING")),
            OptionalElement("nombreComercial", request.Company.TradeName, 300),
            new XElement("ruc", RequiredNumeric(request.Company.Ruc, 13, "INVALID_ISSUER_RUC")),
            new XElement("claveAcceso", RequiredNumeric(sale.AccessKey, 49, "INVALID_SRI_DOCUMENT_CONTEXT")),
            new XElement("codDoc", "01"),
            new XElement("estab", RequiredNumeric(sale.EstablishmentCodeSnapshot, 3, "INVALID_SRI_DOCUMENT_CONTEXT")),
            new XElement("ptoEmi", RequiredNumeric(sale.EmissionPointCodeSnapshot, 3, "INVALID_SRI_DOCUMENT_CONTEXT")),
            new XElement("secuencial", sale.Sequential!.Value.ToString("000000000", CultureInfo.InvariantCulture)),
            new XElement("dirMatriz", matrixAddress),
            ResolveRimpeElement(request.Company.TaxpayerRegime));

        var infoFactura = new XElement("infoFactura",
            new XElement("fechaEmision", sale.DocumentIssuedAt!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
            OptionalElement("dirEstablecimiento", request.Establishment.Address, 300),
            OptionalElement("contribuyenteEspecial", NormalizeSpecialTaxpayerNumber(request.Company.SpecialTaxpayerNumber), 13),
            new XElement("obligadoContabilidad", request.Company.IsAccountingRequired ? "SI" : "NO"),
            new XElement("tipoIdentificacionComprador", buyer.IdentificationType),
            new XElement("razonSocialComprador", buyer.Name),
            new XElement("identificacionComprador", buyer.Identification),
            new XElement("totalSinImpuestos", FormatMoney(sale.Subtotal)),
            new XElement("totalDescuento", FormatMoney(sale.DiscountAmount)),
            BuildTaxTotals(sale),
            new XElement("propina", "0.00"),
            new XElement("importeTotal", FormatMoney(sale.Total)),
            new XElement("moneda", "DOLAR"),
            new XElement("pagos",
                new XElement("pago",
                    new XElement("formaPago", ResolveSriPaymentMethod(sale.PaymentMethod)),
                    new XElement("total", FormatMoney(sale.Total)))));

        var invoice = new XElement("factura",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.1.0"),
            infoTributaria,
            infoFactura,
            new XElement("detalles",
                sale.Items
                    .OrderBy(item => item.Id)
                    .Select(item => BuildDetail(request, item))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), invoice)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static void ValidateInvoiceContext(Sale sale, int environment, int emissionType)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice
            || string.IsNullOrWhiteSpace(sale.AccessKey)
            || string.IsNullOrWhiteSpace(sale.EstablishmentCodeSnapshot)
            || string.IsNullOrWhiteSpace(sale.EmissionPointCodeSnapshot)
            || sale.Sequential is null
            || sale.DocumentIssuedAt is null)
        {
            throw new InvalidOperationException("SRI_XML_DRAFT_GENERATION_FAILED");
        }

        if (environment is not 1 and not 2 || emissionType != 1)
        {
            throw new InvalidOperationException("INVALID_SRI_DOCUMENT_CONTEXT");
        }

        if (sale.Items.Count == 0)
        {
            throw new InvalidOperationException("SRI_XML_REQUIRED_FIELD_MISSING");
        }
    }

    private static BuyerSnapshot ResolveBuyer(Customer? customer)
    {
        if (customer is null)
        {
            return new BuyerSnapshot("07", "CONSUMIDOR FINAL", "9999999999999");
        }

        var name = RequiredText(customer.Name, 300, "INVALID_SRI_CUSTOMER_IDENTIFICATION");
        var identification = RequiredText(customer.Identification, 20, "INVALID_SRI_CUSTOMER_IDENTIFICATION");
        var normalizedIdentification = identification.Trim();
        var digitsOnly = normalizedIdentification.All(char.IsDigit);

        if (digitsOnly && normalizedIdentification == "9999999999999")
        {
            return new BuyerSnapshot("07", name, normalizedIdentification);
        }

        if (digitsOnly && normalizedIdentification.Length == 13)
        {
            return new BuyerSnapshot("04", name, normalizedIdentification);
        }

        if (digitsOnly && normalizedIdentification.Length == 10)
        {
            return new BuyerSnapshot("05", name, normalizedIdentification);
        }

        if (normalizedIdentification.Length <= 20 && normalizedIdentification.All(char.IsLetterOrDigit))
        {
            return new BuyerSnapshot("06", name, normalizedIdentification);
        }

        throw new InvalidOperationException("SRI_BUYER_IDENTIFICATION_TYPE_REQUIRED");
    }

    private static XElement BuildTaxTotals(Sale sale)
    {
        var totals = sale.Items
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
            .Select(group => BuildTaxTotal(group.PercentageCode, group.Base, group.Tax))
            .ToList();

        if (totals.Count == 0)
        {
            throw new InvalidOperationException("SRI_XML_REQUIRED_FIELD_MISSING");
        }

        return new XElement("totalConImpuestos", totals);
    }

    private static XElement BuildTaxTotal(string percentageCode, decimal taxableBase, decimal value)
    {
        return new XElement("totalImpuesto",
            new XElement("codigo", "2"),
            new XElement("codigoPorcentaje", percentageCode),
            new XElement("baseImponible", FormatMoney(taxableBase)),
            new XElement("valor", FormatMoney(value)));
    }

    private static XElement BuildDetail(SriXmlDraftRequest request, SaleItem item)
    {
        var product = ResolveProduct(request, item.ProductId);
        var productCodes = ResolveProductCodes(product);
        var vatMapping = ResolveVatMapping(item.VatCategory);

        return new XElement("detalle",
            new XElement("codigoPrincipal", productCodes.MainCode),
            productCodes.AuxiliaryCode is null ? null : new XElement("codigoAuxiliar", productCodes.AuxiliaryCode),
            new XElement("descripcion", RequiredText(product.Name, 300, "SRI_XML_REQUIRED_FIELD_MISSING", allowSafeTruncate: true)),
            new XElement("cantidad", FormatQuantity(item.Quantity)),
            new XElement("precioUnitario", FormatUnitPrice(item.UnitPrice)),
            new XElement("descuento", FormatMoney(item.DiscountAmount)),
            new XElement("precioTotalSinImpuesto", FormatMoney(item.TaxableSubtotal)),
            new XElement("impuestos",
                new XElement("impuesto",
                    new XElement("codigo", "2"),
                    new XElement("codigoPorcentaje", vatMapping.PercentageCode),
                    new XElement("tarifa", FormatTaxRate(vatMapping.Rate)),
                    new XElement("baseImponible", FormatMoney(item.TaxableSubtotal)),
                    new XElement("valor", FormatMoney(item.TaxAmount)))));
    }

    private static SriXmlProductSnapshot ResolveProduct(SriXmlDraftRequest request, int productId)
    {
        if (request.Products.TryGetValue(productId, out var product))
        {
            return product;
        }

        return new SriXmlProductSnapshot
        {
            ProductId = productId,
            Name = $"Producto {productId}"
        };
    }

    private static ProductCodeSnapshot ResolveProductCodes(SriXmlProductSnapshot product)
    {
        var internalCode = OptionalCode(product.InternalCode);
        var barcode = OptionalCode(product.Barcode);
        var fallback = product.ProductId.ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(internalCode))
        {
            return new ProductCodeSnapshot(
                internalCode,
                barcode is not null && !string.Equals(barcode, internalCode, StringComparison.Ordinal)
                    ? barcode
                    : null);
        }

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            return new ProductCodeSnapshot(barcode, null);
        }

        if (fallback.Length <= MaxProductCodeLength)
        {
            return new ProductCodeSnapshot(fallback, null);
        }

        throw new InvalidOperationException("INVALID_SRI_PRODUCT_CODE");
    }

    private static (string PercentageCode, decimal Rate) ResolveVatMapping(ProductVatCategory category)
    {
        return category switch
        {
            ProductVatCategory.Vat15 => ("4", 15m),
            ProductVatCategory.Vat5 => ("5", 5m),
            ProductVatCategory.Vat0 => ("0", 0m),
            ProductVatCategory.VatNotSubject => ("6", 0m),
            ProductVatCategory.VatExempt => ("7", 0m),
            _ => throw new InvalidOperationException("INVALID_PRODUCT_VAT_CATEGORY")
        };
    }

    private static string ResolveSriPaymentMethod(SalePaymentMethod paymentMethod)
    {
        return paymentMethod switch
        {
            SalePaymentMethod.Cash => "01",
            SalePaymentMethod.Card => "19",
            SalePaymentMethod.Transfer => "20",
            SalePaymentMethod.Other => "20",
            _ => throw new InvalidOperationException("INVALID_SRI_PAYMENT_METHOD")
        };
    }

    private static XElement? OptionalElement(string name, string? value, int maxLength)
    {
        var normalized = OptionalText(value, maxLength);
        return normalized is null ? null : new XElement(name, normalized);
    }

    private static XElement? ResolveRimpeElement(string? taxpayerRegime)
    {
        var normalized = NormalizeText(taxpayerRegime);
        if (normalized is null || !normalized.Contains("RIMPE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new XElement("contribuyenteRimpe", "CONTRIBUYENTE RÉGIMEN RIMPE");
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
            throw new InvalidOperationException("SRI_XML_INVALID_FIELD_FORMAT");
        }

        return normalized;
    }

    private static string RequiredNumeric(string? value, int expectedLength, string errorCode)
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
        string errorCode,
        bool allowSafeTruncate = false)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            throw new InvalidOperationException(errorCode);
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        if (allowSafeTruncate)
        {
            return normalized[..maxLength];
        }

        throw new InvalidOperationException("SRI_XML_INVALID_FIELD_FORMAT");
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

    private static string? OptionalCode(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= MaxProductCodeLength
            ? normalized
            : normalized[..MaxProductCodeLength];
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
            throw new InvalidOperationException("SRI_XML_INVALID_FIELD_FORMAT");
        }

        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(decimal value)
        => FormatFlexibleDecimal(value);

    private static string FormatUnitPrice(decimal value)
        => FormatFlexibleDecimal(value);

    private static string FormatFlexibleDecimal(decimal value)
    {
        if (value < 0m)
        {
            throw new InvalidOperationException("SRI_XML_INVALID_FIELD_FORMAT");
        }

        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        var formatted = rounded.ToString("0.######", CultureInfo.InvariantCulture);
        return formatted.Contains('.', StringComparison.Ordinal) ? formatted : $"{formatted}.00";
    }

    private static string FormatTaxRate(decimal rate)
        => rate.ToString("0.##", CultureInfo.InvariantCulture);

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

    private sealed record BuyerSnapshot(string IdentificationType, string Name, string Identification);

    private sealed record ProductCodeSnapshot(string MainCode, string? AuxiliaryCode);
}
