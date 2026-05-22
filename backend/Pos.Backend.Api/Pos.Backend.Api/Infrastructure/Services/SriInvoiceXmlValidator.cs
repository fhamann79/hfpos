using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriInvoiceXmlValidator : ISriInvoiceXmlValidator
{
    private static readonly Regex NumericRegex = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled);

    public void ValidateUnsignedInvoiceXml(string xml)
    {
        try
        {
            var document = LoadDocument(xml);
            var root = document.Root
                ?? throw ValidationFailure("Root element factura is required.");

            if (root.Name.LocalName != "factura")
            {
                Fail("Root element factura is required.");
            }

            if (root.Attribute("id")?.Value != "comprobante" || root.Attribute("version")?.Value != "1.1.0")
            {
                Fail("Factura id comprobante and version 1.1.0 are required.");
            }

            AssertOrder(root, new[] { "infoTributaria", "infoFactura", "detalles", "infoAdicional" });

            var infoTributaria = RequiredElement(root, "infoTributaria");
            ValidateInfoTributaria(infoTributaria);

            var infoFactura = RequiredElement(root, "infoFactura");
            ValidateInfoFactura(infoFactura);

            var detalles = RequiredElement(root, "detalles");
            ValidateDetalles(detalles);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SRI_XML_SCHEMA_VALIDATION_FAILED")
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("SRI_XML_SCHEMA_VALIDATION_FAILED", ex);
        }
    }

    private static XDocument LoadDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            Fail("XML content is required.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
    }

    private static void ValidateInfoTributaria(XElement element)
    {
        AssertOrder(element, new[]
        {
            "ambiente",
            "tipoEmision",
            "razonSocial",
            "nombreComercial",
            "ruc",
            "claveAcceso",
            "codDoc",
            "estab",
            "ptoEmi",
            "secuencial",
            "dirMatriz",
            "agenteRetencion",
            "contribuyenteRimpe"
        });

        RequiredText(element, "ambiente");
        RequiredText(element, "tipoEmision");
        RequiredText(element, "razonSocial");
        RequiredNumeric(element, "ruc");
        RequiredNumeric(element, "claveAcceso");
        RequiredText(element, "codDoc");
        RequiredNumeric(element, "estab");
        RequiredNumeric(element, "ptoEmi");
        RequiredNumeric(element, "secuencial");
        RequiredText(element, "dirMatriz");
    }

    private static void ValidateInfoFactura(XElement element)
    {
        AssertOrder(element, new[]
        {
            "fechaEmision",
            "dirEstablecimiento",
            "contribuyenteEspecial",
            "obligadoContabilidad",
            "tipoIdentificacionComprador",
            "razonSocialComprador",
            "identificacionComprador",
            "direccionComprador",
            "totalSinImpuestos",
            "totalDescuento",
            "totalConImpuestos",
            "propina",
            "importeTotal",
            "moneda",
            "pagos"
        });

        RequiredText(element, "fechaEmision");
        RequiredText(element, "tipoIdentificacionComprador");
        RequiredText(element, "razonSocialComprador");
        RequiredText(element, "identificacionComprador");
        RequiredNumeric(element, "totalSinImpuestos");
        RequiredNumeric(element, "totalDescuento");

        var totalConImpuestos = RequiredElement(element, "totalConImpuestos");
        var totalImpuestos = totalConImpuestos.Elements().Where(e => e.Name.LocalName == "totalImpuesto").ToList();
        if (totalImpuestos.Count == 0)
        {
            Fail("At least one totalImpuesto is required.");
        }

        foreach (var totalImpuesto in totalImpuestos)
        {
            ValidateTotalTaxElement(totalImpuesto);
        }

        RequiredNumeric(element, "propina");
        RequiredNumeric(element, "importeTotal");
        RequiredText(element, "moneda");

        var pagos = RequiredElement(element, "pagos");
        var pago = pagos.Elements().FirstOrDefault(e => e.Name.LocalName == "pago")
            ?? FailElement("pago is required.");
        RequiredText(pago, "formaPago");
        RequiredNumeric(pago, "total");
    }

    private static void ValidateDetalles(XElement element)
    {
        var detalles = element.Elements().Where(e => e.Name.LocalName == "detalle").ToList();
        if (detalles.Count == 0)
        {
            Fail("At least one detalle is required.");
        }

        foreach (var detalle in detalles)
        {
            AssertOrder(detalle, new[]
            {
                "codigoPrincipal",
                "codigoAuxiliar",
                "descripcion",
                "cantidad",
                "precioUnitario",
                "descuento",
                "precioTotalSinImpuesto",
                "impuestos"
            });

            RequiredText(detalle, "codigoPrincipal");
            RequiredText(detalle, "descripcion");
            RequiredNumeric(detalle, "cantidad");
            RequiredNumeric(detalle, "precioUnitario");
            RequiredNumeric(detalle, "descuento");
            RequiredNumeric(detalle, "precioTotalSinImpuesto");

            var impuestos = RequiredElement(detalle, "impuestos");
            var impuesto = impuestos.Elements().FirstOrDefault(e => e.Name.LocalName == "impuesto")
                ?? FailElement("detalle/impuestos/impuesto is required.");
            ValidateDetailTaxElement(impuesto);
        }
    }

    private static void ValidateTotalTaxElement(XElement element)
    {
        AssertOrder(element, new[] { "codigo", "codigoPorcentaje", "descuentoAdicional", "baseImponible", "valor" });

        RequiredText(element, "codigo");
        RequiredText(element, "codigoPorcentaje");
        RequiredNumeric(element, "baseImponible");
        RequiredNumeric(element, "valor");
    }

    private static void ValidateDetailTaxElement(XElement element)
    {
        AssertOrder(element, new[] { "codigo", "codigoPorcentaje", "tarifa", "baseImponible", "valor" });

        RequiredText(element, "codigo");
        RequiredText(element, "codigoPorcentaje");
        RequiredNumeric(element, "tarifa");
        RequiredNumeric(element, "baseImponible");
        RequiredNumeric(element, "valor");
    }

    private static XElement RequiredElement(XElement parent, string name)
    {
        return parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)
            ?? FailElement($"{name} is required.");
    }

    private static void RequiredText(XElement parent, string name)
    {
        var value = RequiredElement(parent, name).Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            Fail($"{name} cannot be blank.");
        }
    }

    private static void RequiredNumeric(XElement parent, string name)
    {
        var value = RequiredElement(parent, name).Value.Trim();
        if (!NumericRegex.IsMatch(value) || value.Contains(',', StringComparison.Ordinal))
        {
            Fail($"{name} must be numeric with dot decimal separator.");
        }
    }

    private static void AssertOrder(XElement parent, IReadOnlyList<string> expectedOrder)
    {
        var indexByName = expectedOrder
            .Select((name, index) => new { name, index })
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        var lastIndex = -1;

        foreach (var child in parent.Elements())
        {
            if (!indexByName.TryGetValue(child.Name.LocalName, out var index))
            {
                Fail($"Unexpected element {child.Name.LocalName} inside {parent.Name.LocalName}.");
            }

            if (index < lastIndex)
            {
                Fail($"Element {child.Name.LocalName} is out of order inside {parent.Name.LocalName}.");
            }

            lastIndex = index;
        }
    }

    private static void Fail(string detail)
    {
        throw new InvalidOperationException(
            "SRI_XML_SCHEMA_VALIDATION_FAILED",
            new InvalidOperationException(detail));
    }

    private static InvalidOperationException ValidationFailure(string detail)
        => new(
            "SRI_XML_SCHEMA_VALIDATION_FAILED",
            new InvalidOperationException(detail));

    private static XElement FailElement(string detail)
    {
        Fail(detail);
        return null!;
    }
}
