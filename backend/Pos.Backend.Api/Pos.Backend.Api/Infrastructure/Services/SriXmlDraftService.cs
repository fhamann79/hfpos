using System.Globalization;
using System.Xml.Linq;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriXmlDraftService : ISriXmlDraftService
{
    public string GenerateInvoiceXmlDraft(SriXmlDraftRequest request)
    {
        var sale = request.Sale;

        if (sale.DocumentType != SaleDocumentType.Invoice
            || string.IsNullOrWhiteSpace(sale.AccessKey)
            || string.IsNullOrWhiteSpace(sale.EstablishmentCodeSnapshot)
            || string.IsNullOrWhiteSpace(sale.EmissionPointCodeSnapshot)
            || sale.Sequential is null
            || sale.DocumentIssuedAt is null)
        {
            throw new InvalidOperationException("SRI_XML_DRAFT_GENERATION_FAILED");
        }

        var buyerName = request.Customer?.Name?.Trim();
        var buyerIdentification = request.Customer?.Identification?.Trim();

        if (request.Customer is null)
        {
            buyerName = "CONSUMIDOR FINAL";
            buyerIdentification = "9999999999999";
        }
        else if (string.IsNullOrWhiteSpace(buyerName) || string.IsNullOrWhiteSpace(buyerIdentification))
        {
            throw new InvalidOperationException("INVALID_SRI_CUSTOMER_IDENTIFICATION");
        }

        var invoice = new XElement("factura",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.0.0"),
            new XElement("infoTributaria",
                new XElement("ambiente", request.Environment.ToString(CultureInfo.InvariantCulture)),
                new XElement("tipoEmision", request.EmissionType.ToString(CultureInfo.InvariantCulture)),
                new XElement("razonSocial", request.Company.Name),
                new XElement("ruc", request.Company.Ruc),
                new XElement("claveAcceso", sale.AccessKey),
                new XElement("codDoc", "01"),
                new XElement("estab", sale.EstablishmentCodeSnapshot),
                new XElement("ptoEmi", sale.EmissionPointCodeSnapshot),
                new XElement("secuencial", sale.Sequential.Value.ToString("000000000", CultureInfo.InvariantCulture))),
            new XElement("infoFactura",
                new XElement("fechaEmision", sale.DocumentIssuedAt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                new XElement("dirEstablecimiento", request.Establishment.Address),
                new XElement("razonSocialComprador", buyerName),
                new XElement("identificacionComprador", buyerIdentification),
                new XElement("totalSinImpuestos", FormatMoney(sale.Subtotal)),
                new XElement("totalDescuento", FormatMoney(sale.DiscountAmount)),
                BuildTaxTotals(sale),
                new XElement("importeTotal", FormatMoney(sale.Total))),
            new XElement("detalles",
                sale.Items
                    .OrderBy(item => item.Id)
                    .Select(item => new XElement("detalle",
                        new XElement("codigoPrincipal", item.ProductId.ToString(CultureInfo.InvariantCulture)),
                        new XElement("descripcion", ResolveProductName(request, item.ProductId)),
                        new XElement("cantidad", item.Quantity.ToString("0.####", CultureInfo.InvariantCulture)),
                        new XElement("precioUnitario", FormatMoney(item.UnitPrice)),
                        new XElement("descuento", FormatMoney(item.DiscountAmount)),
                        new XElement("precioTotalSinImpuesto", FormatMoney(item.TaxableSubtotal)),
                        new XElement("impuestos",
                            new XElement("impuesto",
                                new XElement("codigo", "2"),
                                new XElement("codigoPorcentaje", GetDraftVatCode(item.VatCategory)),
                                new XElement("tarifa", FormatPercent(item.VatRate)),
                                new XElement("baseImponible", FormatMoney(item.TaxableSubtotal)),
                                new XElement("valor", FormatMoney(item.TaxAmount))))))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), invoice).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement BuildTaxTotals(Core.Entities.Sale sale)
    {
        return new XElement("totalConImpuestos",
            BuildTaxTotal("4", 15m, sale.Vat15Subtotal, sale.Items.Where(item => item.VatCategory == ProductVatCategory.Vat15).Sum(item => item.TaxAmount)),
            BuildTaxTotal("5", 5m, sale.Vat5Subtotal, sale.Items.Where(item => item.VatCategory == ProductVatCategory.Vat5).Sum(item => item.TaxAmount)),
            BuildTaxTotal("0", 0m, sale.Vat0Subtotal, 0m),
            BuildTaxTotal("6", 0m, sale.VatExemptSubtotal, 0m),
            BuildTaxTotal("7", 0m, sale.VatNotSubjectSubtotal, 0m));
    }

    private static XElement BuildTaxTotal(string percentageCode, decimal rate, decimal taxableBase, decimal value)
    {
        return new XElement("totalImpuesto",
            new XElement("codigo", "2"),
            new XElement("codigoPorcentaje", percentageCode),
            new XElement("baseImponible", FormatMoney(taxableBase)),
            new XElement("tarifa", rate.ToString("0.##", CultureInfo.InvariantCulture)),
            new XElement("valor", FormatMoney(value)));
    }

    private static string ResolveProductName(SriXmlDraftRequest request, int productId)
    {
        return request.ProductNames.TryGetValue(productId, out var name)
            ? name
            : $"Producto {productId}";
    }

    private static string GetDraftVatCode(ProductVatCategory category)
    {
        return category switch
        {
            ProductVatCategory.Vat15 => "4",
            ProductVatCategory.Vat5 => "5",
            ProductVatCategory.Vat0 => "0",
            ProductVatCategory.VatExempt => "6",
            ProductVatCategory.VatNotSubject => "7",
            _ => "0"
        };
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal rate)
    {
        return (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
