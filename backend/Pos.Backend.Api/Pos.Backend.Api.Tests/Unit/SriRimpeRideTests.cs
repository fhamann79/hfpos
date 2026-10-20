using System.Reflection;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SriRimpeRideTests
{
    private const string Generic = "CONTRIBUYENTE R\u00c9GIMEN RIMPE";
    private const string Popular = "CONTRIBUYENTE NEGOCIO POPULAR - R\u00c9GIMEN RIMPE";
    private const string AccessKey = "2010202601179000000000110010010000000011234567811";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object?[]> Regimes()
    {
        foreach (var label in new[] { "RIMPE", "Regimen RIMPE", Generic,
            "RIMPE - Emprendedor", "Emprendedor RIMPE", "R\u00e9gimen RIMPE (Emprendedor)",
            "Contribuyente RIMPE - Emprendedor" })
            yield return new object?[] { label, Generic };
        foreach (var label in new[] { Popular, "NEGOCIO POPULAR", "RIMPE - Negocio Popular",
            "negocio popular rimpe", "  R\u00e9gimen  RIMPE (Negocio Popular)  ",
            "Contribuyente RIMPE Negocio Popular" })
            yield return new object?[] { label, Popular };
        foreach (var label in new string?[] { null, "", "GENERAL", "MICROEMPRESAS", "NO RIMPE",
            "NO NEGOCIO POPULAR", "NO ES RIMPE NEGOCIO POPULAR", "GENERAL (NO RIMPE)",
            "EX RIMPE", "RIMPE NO APLICA", "SUPERIMPE", "OTRO RIMPE EMPRENDEDOR",
            "GENERAL / RIMPE", "RIMPE EMPRENDEDOR NEGOCIO POPULAR" })
            yield return new object?[] { label, null };
    }

    [Theory]
    [MemberData(nameof(Regimes))]
    public void Invoice_and_credit_note_emit_only_explicit_regime_labels(string? label, string? expected)
    {
        foreach (var credit in new[] { false, true })
        {
            var xml = Draft(credit, label);
            ValidateLocal(credit, xml.ToString());
            var info = xml.Element("infoTributaria")!;
            var legend = info.Element("contribuyenteRimpe");
            Assert.Equal(expected, legend?.Value);
            Assert.Equal(expected is null ? 0 : 1, xml.Descendants("contribuyenteRimpe").Count());
            if (legend is not null)
            {
                Assert.Same(info.Elements().Last(), legend);
                Assert.Equal("dirMatriz", legend.PreviousNode is XElement previous ? previous.Name.LocalName : null);
                Assert.Equal(expected == Popular ? 45 : 27, legend.Value.Length);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Regime_legend_never_overrides_explicit_tax_snapshots(bool credit)
    {
        // Intentionally inconsistent synthetic input: preservation is not tax certification.
        var xml = Draft(credit, "NEGOCIO POPULAR", vat15: true);
        Assert.Equal("4", xml.Descendants("impuesto").Single().Element("codigoPorcentaje")!.Value);
        Assert.Equal("15", xml.Descendants("impuesto").Single().Element("tarifa")!.Value);
        var ride = BuildRide(credit, xml);
        Assert.Equal(1.5m, ride.Totals.TaxAmount);
        Assert.Equal(11.5m, ride.Totals.Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ride_preserves_authorized_canonical_and_legacy_values_without_live_company(bool credit)
    {
        var xml = Draft(credit, "RIMPE");
        var info = xml.Element("infoTributaria")!;
        var legacy = xml.Element(credit ? "infoNotaCredito" : "infoFactura")!;
        info.Element("contribuyenteRimpe")!.Value = Popular;
        legacy.Add(new XElement("contribuyenteRimpe", "LEGACY VALUE UNCHANGED"));
        Assert.Equal(Popular, BuildRide(credit, xml).Issuer.TaxpayerRegime);
        info.Element("contribuyenteRimpe")!.Value = "HISTORICAL XML VALUE";
        Assert.Equal("HISTORICAL XML VALUE", BuildRide(credit, xml).Issuer.TaxpayerRegime);
        info.Element("contribuyenteRimpe")!.Remove();
        Assert.Equal("LEGACY VALUE UNCHANGED", BuildRide(credit, xml).Issuer.TaxpayerRegime);
        legacy.Element("contribuyenteRimpe")!.Remove();
        if (!credit)
        {
            legacy.Add(new XElement("regimenMicroempresas", "HISTORICAL MICROEMPRESAS"));
            Assert.Equal("HISTORICAL MICROEMPRESAS", BuildRide(false, xml).Issuer.TaxpayerRegime);
            legacy.Element("regimenMicroempresas")!.Remove();
        }
        Assert.Null(BuildRide(credit, xml).Issuer.TaxpayerRegime);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Pdf_preserves_measured_code_legend_and_long_detail_across_pages(bool credit, bool multipage)
    {
        var ride = BuildRide(credit, Draft(credit, "NEGOCIO POPULAR"));
        ride.Branding.PrimaryColor = "#157347";
        ride.Issuer.TradeName = "COMERCIO SINTETICO";
        ride.Issuer.MatrixAddress = "MATRIZ SINTETICA - Avenida de prueba 123";
        ride.Issuer.EstablishmentAddress = "LOCAL SINTETICO - Calle de prueba 456";
        var description = "INICIO DESCRIPCION\n" + string.Join("\n", Enumerable.Range(1, multipage ? 95 : 8)
            .Select(i => $"Linea {i:000} descripcion sintetica completa"))
            + "\n" + new string('W', multipage ? 450 : 70) + "\nFIN DESCRIPCION";
        ride.Items[0].MainCode = "7861234567890";
        ride.Items[0].Description = description;
        if (multipage)
        {
            ride.Items.Add(new SriRideItemDto { MainCode = new string('9', 100),
                Description = "ITEM FINAL", Quantity = 1, UnitPrice = 10, Subtotal = 10, LineTotal = 10 });
            ride.Totals.SubtotalWithoutTaxes += 10;
            ride.Totals.Vat0Subtotal += 10;
            ride.Totals.Total += 10;
            if (ride.Payments.Count > 0) ride.Payments[0].Amount += 10;
        }

        var renderer = NewRenderer(ride);
        var bytes = (byte[])renderer.GetType().GetMethod("Render")!.Invoke(renderer, null)!;
        var columns = (double[])Invoke(renderer, "ItemColumns")!;
        var codeFont = (XFont)renderer.GetType().GetField("_smallFont", PrivateInstance)!.GetValue(renderer)!;
        var bodyFont = (XFont)renderer.GetType().GetField("_bodyFont", PrivateInstance)!.GetValue(renderer)!;
        using (var measurement = new PdfDocument())
        using (var graphics = XGraphics.FromPdfPage(measurement.AddPage()))
        {
            Assert.True(graphics.MeasureString("7861234567890", codeFont).Width <= columns[0] - 6);
        }
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(bytes.Length > 1000);
        Assert.True(multipage ? pdf.PageCount >= 3 : pdf.PageCount == 1);

        // Wrap and measurement must agree, including explicit newlines and oversized tokens.
        var measured = NewRenderer(ride);
        Invoke(measured, "NewPage", false);
        try
        {
            var lines = (IReadOnlyList<string>)Invoke(measured, "WrapTextByWidth", description, bodyFont, columns[1] - 6)!;
            Assert.Equal(string.Concat(description.Where(c => !char.IsWhiteSpace(c))),
                string.Concat(string.Concat(lines).Where(c => !char.IsWhiteSpace(c))));
            var graphics = (XGraphics)measured.GetType().GetField("_gfx", PrivateInstance)!.GetValue(measured)!;
            Assert.All(lines, line => Assert.True(graphics.MeasureString(line, bodyFont).Width <= columns[1] - 6));
            var legendLines = (IReadOnlyList<string>)Invoke(measured, "WrapTextByWidth", Popular, bodyFont, 330d)!;
            Assert.Equal(Popular, string.Join(" ", legendLines));
        }
        finally
        {
            ((XGraphics)measured.GetType().GetField("_gfx", PrivateInstance)!.GetValue(measured)!).Dispose();
        }

        var output = Environment.GetEnvironmentVariable("HFPOS_RIMPE_PDF_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, $"{(credit ? "credit-note" : "invoice")}-{(multipage ? "multipage" : "ean13-popular")}.pdf"), bytes);
        }
    }

    [Fact]
    public void Pdf_row_moves_to_next_page_when_less_than_minimum_height_remains()
    {
        var renderer = NewRenderer(new SriRideDto());
        Invoke(renderer, "NewPage", false);
        var type = renderer.GetType();
        var bottom = (double)type.GetProperty("BottomY", PrivateInstance)!.GetValue(renderer)!;
        type.GetField("_y", PrivateInstance)!.SetValue(renderer, bottom - 20);
        try
        {
            Invoke(renderer, "DrawItemRows", new SriRideItemDto { MainCode = "123", Description = "Fila corta" });
            var document = (PdfDocument)type.GetField("_document", PrivateInstance)!.GetValue(renderer)!;
            Assert.Equal(2, document.PageCount);
            Assert.True((double)type.GetField("_y", PrivateInstance)!.GetValue(renderer)! <= bottom);
        }
        finally
        {
            ((XGraphics)type.GetField("_gfx", PrivateInstance)!.GetValue(renderer)!).Dispose();
        }
    }

    private static object NewRenderer(SriRideDto ride)
    {
        var assembly = typeof(SriRidePdfService).Assembly;
        assembly.GetType("Pos.Backend.Api.Infrastructure.Services.SriRidePdfFontResolver")!
            .GetMethod("Register")!.Invoke(null, null);
        return Activator.CreateInstance(typeof(SriRidePdfService)
            .GetNestedType("RidePdfRenderer", BindingFlags.NonPublic)!, ride)!;
    }

    private static object? Invoke(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);

    private static void ValidateLocal(bool credit, string xml)
    {
        if (credit && !xml.StartsWith("<?xml", StringComparison.Ordinal))
            xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + xml;
        if (credit) new SriCreditNoteXmlValidator().ValidateUnsignedCreditNoteXml(xml);
        else new SriInvoiceXmlValidator().ValidateUnsignedInvoiceXml(xml);
    }

    private static SriRideDto BuildRide(bool credit, XElement xml)
    {
        var authorization = new XElement("autorizacion",
            new XElement("estado", "AUTORIZADO"),
            new XElement("numeroAutorizacion", AccessKey),
            new XElement("fechaAutorizacion", "2026-10-20T15:00:00Z"),
            new XElement("comprobante", new XCData(xml.ToString())));
        var type = credit ? typeof(SriCreditNoteSubmissionService) : typeof(SriSubmissionService);
        // A changed live company must never reinterpret the authorized XML.
        object entity = credit ? Credit() : Sale();
        return (SriRideDto)type.GetMethod("BuildRide", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new[] { entity, authorization })!;
    }

    private static XElement Draft(bool credit, string? regime, bool vat15 = false)
    {
        var company = new Company { Name = "EMISOR SINTETICO", Ruc = "1790000000001",
            MatrixAddress = "Direccion sintetica", TaxpayerRegime = regime };
        var establishment = new Establishment { Address = "Local sintetico" };
        var xml = credit
            ? new SriCreditNoteXmlDraftService().GenerateCreditNoteXmlDraft(new SriCreditNoteXmlDraftRequest
            {
                CreditNote = Credit(vat15), Company = company, Establishment = establishment,
                Environment = 1, EmissionType = 1, FiscalEmissionDate = new(2026, 10, 20),
                OriginalInvoiceFiscalEmissionDate = new(2026, 10, 19)
            })
            : new SriXmlDraftService().GenerateInvoiceXmlDraft(new SriXmlDraftRequest
            {
                Sale = Sale(vat15), Company = company, Establishment = establishment, Environment = 1, EmissionType = 1,
                Products = new Dictionary<int, SriXmlProductSnapshot> { [1] = new()
                    { ProductId = 1, Name = "Producto sintetico", Barcode = "7861234567890" } }
            });
        ValidateLocal(credit, xml);
        return XElement.Parse(xml);
    }

    private static Sale Sale(bool vat15 = false) => new()
    {
        Id = 1, Company = new Company { TaxpayerRegime = "GENERAL LIVE VALUE" },
        DocumentType = SaleDocumentType.Invoice, Number = "001-001-000000001", Sequential = 1,
        EstablishmentCodeSnapshot = "001", EmissionPointCodeSnapshot = "001", AccessKey = AccessKey,
        DocumentIssuedAt = new DateTime(2026, 10, 20), Subtotal = 10, Vat15Subtotal = vat15 ? 10 : 0,
        Vat0Subtotal = vat15 ? 0 : 10, TaxAmount = vat15 ? 1.5m : 0, Total = vat15 ? 11.5m : 10,
        Items = new List<SaleItem> { new() { Id = 1, ProductId = 1, Quantity = 1, UnitPrice = 10,
            TaxableSubtotal = 10, VatCategory = vat15 ? ProductVatCategory.Vat15 : ProductVatCategory.Vat0,
            TaxAmount = vat15 ? 1.5m : 0, LineTotal = vat15 ? 11.5m : 10 } }
    };

    private static CreditNote Credit(bool vat15 = false) => new()
    {
        Id = 1, Company = new Company { TaxpayerRegime = "GENERAL LIVE VALUE" },
        Number = "001-001-000000002", Sequential = 2, EstablishmentCodeSnapshot = "001",
        EmissionPointCodeSnapshot = "001", AccessKey = AccessKey, DocumentIssuedAt = new DateTime(2026, 10, 20),
        BuyerNameSnapshot = "COMPRADOR SINTETICO", BuyerIdentificationTypeSnapshot = "07",
        BuyerIdentificationSnapshot = "9999999999999", Reason = "Devolucion sintetica",
        OriginalSaleNumberSnapshot = "001-001-000000001", OriginalSaleAccessKeySnapshot = AccessKey,
        OriginalSaleAuthorizationNumberSnapshot = AccessKey, OriginalSaleAuthorizedAtSnapshot = new DateTime(2026, 10, 19),
        OriginalSaleDocumentIssuedAtSnapshot = new DateTime(2026, 10, 19),
        Subtotal = 10, Vat15Subtotal = vat15 ? 10 : 0, Vat0Subtotal = vat15 ? 0 : 10,
        TaxAmount = vat15 ? 1.5m : 0, Total = vat15 ? 11.5m : 10,
        Items = new List<CreditNoteItem> { new() { Id = 1, ProductId = 1, ProductNameSnapshot = "Producto sintetico",
            ProductMainCodeSnapshot = "7861234567890", Quantity = 1, UnitPrice = 10, TaxableSubtotal = 10,
            VatCategory = vat15 ? ProductVatCategory.Vat15 : ProductVatCategory.Vat0,
            TaxAmount = vat15 ? 1.5m : 0, LineTotal = vat15 ? 11.5m : 10 } }
    };
}
