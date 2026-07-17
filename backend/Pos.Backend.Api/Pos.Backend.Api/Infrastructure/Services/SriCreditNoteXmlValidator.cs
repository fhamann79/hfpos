using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriCreditNoteXmlValidator : ISriCreditNoteXmlValidator
{
    private const string ValidationErrorCode =
        "SRI_CREDIT_NOTE_XML_SCHEMA_VALIDATION_FAILED";
    private static readonly Regex DigitsRegex =
        new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex MoneyRegex =
        new(@"^\d+\.\d{2}$", RegexOptions.Compiled);
    private static readonly Regex FlexibleDecimalRegex =
        new(@"^\d+(\.\d{1,6})?$", RegexOptions.Compiled);
    private static readonly Regex OriginalDocumentNumberRegex =
        new(@"^\d{3}-\d{3}-\d{9}$", RegexOptions.Compiled);

    public void ValidateUnsignedCreditNoteXml(string xml)
    {
        try
        {
            var document = LoadDocument(xml);
            ValidateDeclaration(document);

            var root = document.Root
                ?? throw ValidationFailure("Root element notaCredito is required.");

            if (root.Name.LocalName != "notaCredito")
            {
                Fail("Root element notaCredito is required.");
            }

            ValidateRootAttributes(root);
            AssertOrder(
                root,
                new[] { "infoTributaria", "infoNotaCredito", "detalles" });
            AssertUniqueChildren(
                root,
                new[] { "infoTributaria", "infoNotaCredito", "detalles" });

            ValidateInfoTributaria(RequiredElement(root, "infoTributaria"));
            ValidateInfoNotaCredito(RequiredElement(root, "infoNotaCredito"));
            ValidateDetalles(RequiredElement(root, "detalles"));
        }
        catch (InvalidOperationException ex)
            when (ex.Message == ValidationErrorCode)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ValidationErrorCode, ex);
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

    private static void ValidateDeclaration(XDocument document)
    {
        if (document.Declaration?.Version != "1.0"
            || !string.Equals(
                document.Declaration.Encoding,
                "UTF-8",
                StringComparison.OrdinalIgnoreCase))
        {
            Fail("XML declaration version 1.0 and UTF-8 encoding are required.");
        }
    }

    private static void ValidateRootAttributes(XElement root)
    {
        var attributes = root.Attributes().ToList();
        if (attributes.Count != 2
            || root.Attribute("id")?.Value != "comprobante"
            || root.Attribute("version")?.Value != "1.1.0"
            || attributes.Any(attribute =>
                attribute.Name.LocalName is not ("id" or "version")))
        {
            Fail("notaCredito id comprobante and version 1.1.0 are required.");
        }
    }

    private static void ValidateInfoTributaria(XElement element)
    {
        var expectedOrder = new[]
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
            "contribuyenteRimpe"
        };

        AssertOrder(element, expectedOrder);
        AssertUniqueChildren(element, expectedOrder);

        var environment = RequiredText(element, "ambiente", 1);
        if (environment is not ("1" or "2"))
        {
            Fail("ambiente must be 1 or 2.");
        }

        if (RequiredText(element, "tipoEmision", 1) != "1")
        {
            Fail("tipoEmision must be 1.");
        }

        RequiredText(element, "razonSocial", 300);
        OptionalText(element, "nombreComercial", 300);
        RequiredDigits(element, "ruc", 13);
        RequiredDigits(element, "claveAcceso", 49);

        if (RequiredText(element, "codDoc", 2) != "04")
        {
            Fail("codDoc must be 04.");
        }

        RequiredDigits(element, "estab", 3);
        RequiredDigits(element, "ptoEmi", 3);

        var sequential = RequiredDigits(element, "secuencial", 9);
        if (!decimal.TryParse(
                sequential,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sequentialValue)
            || sequentialValue <= 0m)
        {
            Fail("secuencial must be greater than zero.");
        }

        RequiredText(element, "dirMatriz", 300);
        OptionalText(element, "contribuyenteRimpe", 50);
    }

    private static void ValidateInfoNotaCredito(XElement element)
    {
        var expectedOrder = new[]
        {
            "fechaEmision",
            "dirEstablecimiento",
            "tipoIdentificacionComprador",
            "razonSocialComprador",
            "identificacionComprador",
            "contribuyenteEspecial",
            "obligadoContabilidad",
            "codDocModificado",
            "numDocModificado",
            "fechaEmisionDocSustento",
            "totalSinImpuestos",
            "valorModificacion",
            "moneda",
            "totalConImpuestos",
            "motivo"
        };

        AssertOrder(element, expectedOrder);
        AssertUniqueChildren(element, expectedOrder);

        RequiredDate(element, "fechaEmision");
        OptionalText(element, "dirEstablecimiento", 300);

        var identificationType = RequiredText(
            element,
            "tipoIdentificacionComprador",
            2);
        RequiredText(element, "razonSocialComprador", 300);
        var identification = RequiredText(
            element,
            "identificacionComprador",
            20);
        ValidateBuyerIdentification(identificationType, identification);

        var specialTaxpayer = OptionalText(
            element,
            "contribuyenteEspecial",
            13);
        if (specialTaxpayer is not null
            && (specialTaxpayer.Length < 3
                || !DigitsRegex.IsMatch(specialTaxpayer)))
        {
            Fail("contribuyenteEspecial must be numeric.");
        }

        var accountingRequired = RequiredText(
            element,
            "obligadoContabilidad",
            2);
        if (accountingRequired is not ("SI" or "NO"))
        {
            Fail("obligadoContabilidad must be SI or NO.");
        }

        if (RequiredText(element, "codDocModificado", 2) != "01")
        {
            Fail("codDocModificado must be 01.");
        }

        var modifiedDocumentNumber = RequiredText(
            element,
            "numDocModificado",
            17);
        if (!OriginalDocumentNumberRegex.IsMatch(modifiedDocumentNumber))
        {
            Fail("numDocModificado has an invalid format.");
        }

        RequiredDate(element, "fechaEmisionDocSustento");
        RequiredMoney(element, "totalSinImpuestos");
        RequiredMoney(element, "valorModificacion");

        if (RequiredText(element, "moneda", 10) != "DOLAR")
        {
            Fail("moneda must be DOLAR.");
        }

        ValidateTaxTotals(RequiredElement(element, "totalConImpuestos"));
        RequiredText(element, "motivo", 300);
    }

    private static void ValidateTaxTotals(XElement element)
    {
        var totalTaxes = element.Elements()
            .Where(child => child.Name.LocalName == "totalImpuesto")
            .ToList();

        if (totalTaxes.Count == 0
            || totalTaxes.Count != element.Elements().Count())
        {
            Fail("At least one totalImpuesto is required.");
        }

        foreach (var totalTax in totalTaxes)
        {
            var expectedOrder = new[]
            {
                "codigo",
                "codigoPorcentaje",
                "baseImponible",
                "valor"
            };

            AssertOrder(totalTax, expectedOrder);
            AssertUniqueChildren(totalTax, expectedOrder);
            ValidateVatCode(totalTax);
            RequiredMoney(totalTax, "baseImponible");
            RequiredMoney(totalTax, "valor");
        }
    }

    private static void ValidateDetalles(XElement element)
    {
        var details = element.Elements()
            .Where(child => child.Name.LocalName == "detalle")
            .ToList();

        if (details.Count == 0 || details.Count != element.Elements().Count())
        {
            Fail("At least one detalle is required.");
        }

        foreach (var detail in details)
        {
            var expectedOrder = new[]
            {
                "codigoInterno",
                "codigoAdicional",
                "descripcion",
                "cantidad",
                "precioUnitario",
                "descuento",
                "precioTotalSinImpuesto",
                "impuestos"
            };

            AssertOrder(detail, expectedOrder);
            AssertUniqueChildren(detail, expectedOrder);

            RequiredText(detail, "codigoInterno", 25);
            OptionalText(detail, "codigoAdicional", 25);
            RequiredText(detail, "descripcion", 300);
            RequiredFlexibleDecimal(detail, "cantidad", requirePositive: true);
            RequiredFlexibleDecimal(
                detail,
                "precioUnitario",
                requirePositive: false);
            RequiredMoney(detail, "descuento");
            RequiredMoney(detail, "precioTotalSinImpuesto");
            ValidateDetailTaxes(RequiredElement(detail, "impuestos"));
        }
    }

    private static void ValidateDetailTaxes(XElement element)
    {
        var taxes = element.Elements()
            .Where(child => child.Name.LocalName == "impuesto")
            .ToList();

        if (taxes.Count != 1 || taxes.Count != element.Elements().Count())
        {
            Fail("Each detail must contain exactly one impuesto.");
        }

        var tax = taxes[0];
        var expectedOrder = new[]
        {
            "codigo",
            "codigoPorcentaje",
            "tarifa",
            "baseImponible",
            "valor"
        };

        AssertOrder(tax, expectedOrder);
        AssertUniqueChildren(tax, expectedOrder);
        var percentageCode = ValidateVatCode(tax);
        var rate = RequiredFlexibleDecimal(
            tax,
            "tarifa",
            requirePositive: false);
        var expectedRate = percentageCode switch
        {
            "4" => 15m,
            "5" => 5m,
            _ => 0m
        };

        if (rate != expectedRate)
        {
            Fail("tarifa does not match codigoPorcentaje.");
        }

        RequiredMoney(tax, "baseImponible");
        RequiredMoney(tax, "valor");
    }

    private static string ValidateVatCode(XElement element)
    {
        if (RequiredText(element, "codigo", 1) != "2")
        {
            Fail("codigo must be 2 for IVA.");
        }

        var percentageCode = RequiredText(
            element,
            "codigoPorcentaje",
            1);
        if (percentageCode is not ("4" or "5" or "0" or "6" or "7"))
        {
            Fail("codigoPorcentaje is invalid.");
        }

        return percentageCode;
    }

    private static void ValidateBuyerIdentification(
        string identificationType,
        string identification)
    {
        var isValid = identificationType switch
        {
            "04" => identification.Length == 13 && DigitsRegex.IsMatch(identification),
            "05" => identification.Length == 10 && DigitsRegex.IsMatch(identification),
            "06" => identification.Length <= 20 && identification.All(char.IsLetterOrDigit),
            "07" => identification == "9999999999999",
            _ => false
        };

        if (!isValid)
        {
            Fail("Buyer identification is invalid.");
        }
    }

    private static void RequiredDate(XElement parent, string name)
    {
        var value = RequiredText(parent, name, 10);
        if (!DateTime.TryParseExact(
                value,
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            Fail($"{name} must use dd/MM/yyyy.");
        }
    }

    private static void RequiredMoney(XElement parent, string name)
    {
        var value = RequiredText(parent, name, 50);
        if (!MoneyRegex.IsMatch(value)
            || value.Contains(',', StringComparison.Ordinal)
            || !decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount)
            || amount < 0m)
        {
            Fail($"{name} must be non-negative with dot decimal separator.");
        }
    }

    private static decimal RequiredFlexibleDecimal(
        XElement parent,
        string name,
        bool requirePositive)
    {
        var value = RequiredText(parent, name, 50);
        if (!FlexibleDecimalRegex.IsMatch(value)
            || value.Contains(',', StringComparison.Ordinal))
        {
            Fail($"{name} has an invalid numeric format.");
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount)
            || amount < 0m
            || (requirePositive && amount <= 0m))
        {
            Fail($"{name} has an invalid numeric value.");
        }

        return amount;
    }

    private static string RequiredDigits(
        XElement parent,
        string name,
        int exactLength)
    {
        var value = RequiredText(parent, name, exactLength);
        if (value.Length != exactLength || !DigitsRegex.IsMatch(value))
        {
            Fail($"{name} must contain {exactLength} digits.");
        }

        return value;
    }

    private static string RequiredText(
        XElement parent,
        string name,
        int maxLength)
    {
        var element = RequiredElement(parent, name);
        var value = element.Value.Trim();

        if (value.Length == 0 || value.Length > maxLength)
        {
            Fail($"{name} is blank or exceeds its maximum length.");
        }

        return value;
    }

    private static string? OptionalText(
        XElement parent,
        string name,
        int maxLength)
    {
        var elements = parent.Elements()
            .Where(child => child.Name.LocalName == name)
            .ToList();

        if (elements.Count == 0)
        {
            return null;
        }

        if (elements.Count != 1)
        {
            Fail($"{name} must not be repeated.");
        }

        var value = elements[0].Value.Trim();
        if (value.Length == 0 || value.Length > maxLength)
        {
            Fail($"{name} is blank or exceeds its maximum length.");
        }

        return value;
    }

    private static XElement RequiredElement(XElement parent, string name)
    {
        var elements = parent.Elements()
            .Where(child => child.Name.LocalName == name)
            .ToList();

        if (elements.Count != 1)
        {
            Fail($"{name} is required exactly once.");
        }

        return elements[0];
    }

    private static void AssertUniqueChildren(
        XElement parent,
        IReadOnlyList<string> uniqueNames)
    {
        foreach (var name in uniqueNames)
        {
            if (parent.Elements().Count(child => child.Name.LocalName == name) > 1)
            {
                Fail($"{name} must not be repeated inside {parent.Name.LocalName}.");
            }
        }
    }

    private static void AssertOrder(
        XElement parent,
        IReadOnlyList<string> expectedOrder)
    {
        var indexByName = expectedOrder
            .Select((name, index) => new { name, index })
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);
        var lastIndex = -1;

        foreach (var child in parent.Elements())
        {
            if (!indexByName.TryGetValue(child.Name.LocalName, out var index))
            {
                Fail(
                    $"Unexpected element {child.Name.LocalName} inside {parent.Name.LocalName}.");
            }

            if (index < lastIndex)
            {
                Fail(
                    $"Element {child.Name.LocalName} is out of order inside {parent.Name.LocalName}.");
            }

            lastIndex = index;
        }
    }

    private static void Fail(string detail)
    {
        throw new InvalidOperationException(
            ValidationErrorCode,
            new InvalidOperationException(detail));
    }

    private static InvalidOperationException ValidationFailure(string detail)
        => new(
            ValidationErrorCode,
            new InvalidOperationException(detail));
}
