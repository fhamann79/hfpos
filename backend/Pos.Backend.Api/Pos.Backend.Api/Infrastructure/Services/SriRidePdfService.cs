using System.Globalization;
using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using QRCoder;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SriRidePdfService : ISriRidePdfService
{
    private const string PdfContentType = "application/pdf";

    private readonly ISriSubmissionService _sriSubmissionService;
    private readonly ILogger<SriRidePdfService> _logger;

    public SriRidePdfService(
        ISriSubmissionService sriSubmissionService,
        ILogger<SriRidePdfService> logger)
    {
        _sriSubmissionService = sriSubmissionService;
        _logger = logger;
    }

    public async Task<SriRidePdfFileResult> GenerateAsync(int saleId)
    {
        var ride = await _sriSubmissionService.GetRideAsync(saleId);

        try
        {
            SriRidePdfFontResolver.Register();

            var bytes = new RidePdfRenderer(ride).Render();

            if (!IsPdf(bytes))
            {
                throw new InvalidOperationException("Generated RIDE file is not a PDF.");
            }

            return new SriRidePdfFileResult
            {
                Bytes = bytes,
                ContentType = PdfContentType,
                FileName = BuildFileName(ride)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not generate SRI RIDE PDF. SaleId {SaleId}", saleId);
            throw new InvalidOperationException("SRI_RIDE_PDF_GENERATION_FAILED", ex);
        }
    }

    private static bool IsPdf(byte[] bytes)
        => bytes.Length >= 5
            && bytes[0] == 0x25
            && bytes[1] == 0x50
            && bytes[2] == 0x44
            && bytes[3] == 0x46
            && bytes[4] == 0x2D;

    private static string BuildFileName(SriRideDto ride)
    {
        var identifier = TrimToNull(ride.DocumentNumber) ?? $"sale-{ride.SaleId}";
        var safeIdentifier = SanitizeFileNamePart(identifier);

        return $"{safeIdentifier}-RIDE.pdf";
    }

    private static string SanitizeFileNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '-' ? character : '-');
        }

        var sanitized = builder.ToString().Trim('-');

        return string.IsNullOrWhiteSpace(sanitized) ? "sale" : sanitized;
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class RidePdfRenderer
    {
        private const double Margin = 28;
        private const double Gap = 7;
        private const double FooterReserve = 18;
        private const string DefaultFooterNote = "Representacion impresa de comprobante electronico autorizado.";
        private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");
        private static readonly XStringFormat TopLeft = new()
        {
            Alignment = XStringAlignment.Near,
            LineAlignment = XLineAlignment.Near
        };
        private static readonly XStringFormat TopRight = new()
        {
            Alignment = XStringAlignment.Far,
            LineAlignment = XLineAlignment.Near
        };
        private static readonly XStringFormat Center = new()
        {
            Alignment = XStringAlignment.Center,
            LineAlignment = XLineAlignment.Center
        };

        private readonly SriRideDto _ride;
        private readonly PdfDocument _document = new();
        private readonly XColor _brandColor;
        private readonly XBrush _brandBrush;
        private readonly XBrush _textBrush = new XSolidBrush(XColor.FromArgb(17, 24, 39));
        private readonly XBrush _mutedBrush = new XSolidBrush(XColor.FromArgb(71, 85, 105));
        private readonly XBrush _softBrush = new XSolidBrush(XColor.FromArgb(248, 250, 252));
        private readonly XBrush _darkHeaderBrush = new XSolidBrush(XColor.FromArgb(51, 65, 85));
        private readonly XPen _borderPen = new(XColor.FromArgb(203, 213, 225), 0.7);
        private readonly XPen _softBorderPen = new(XColor.FromArgb(226, 232, 240), 0.6);
        private readonly XPen _brandPen;
        private readonly XFont _titleFont = new(SriRidePdfFontResolver.SansFamily, 15, XFontStyleEx.Bold);
        private readonly XFont _headerFont = new(SriRidePdfFontResolver.SansFamily, 11, XFontStyleEx.Bold);
        private readonly XFont _subHeaderFont = new(SriRidePdfFontResolver.SansFamily, 9, XFontStyleEx.Bold);
        private readonly XFont _bodyFont = new(SriRidePdfFontResolver.SansFamily, 7.5, XFontStyleEx.Regular);
        private readonly XFont _bodyBoldFont = new(SriRidePdfFontResolver.SansFamily, 7.5, XFontStyleEx.Bold);
        private readonly XFont _smallFont = new(SriRidePdfFontResolver.SansFamily, 6.6, XFontStyleEx.Regular);
        private readonly XFont _smallBoldFont = new(SriRidePdfFontResolver.SansFamily, 6.6, XFontStyleEx.Bold);
        private readonly XFont _codeFont = new(SriRidePdfFontResolver.MonoFamily, 7.1, XFontStyleEx.Regular);
        private readonly XFont _compactCodeFont = new(SriRidePdfFontResolver.MonoFamily, 6.2, XFontStyleEx.Regular);

        private PdfPage _page = null!;
        private XGraphics _gfx = null!;
        private XTextFormatter _text = null!;
        private double _y;

        public RidePdfRenderer(SriRideDto ride)
        {
            _ride = ride;
            _brandColor = ParseBrandColor(ride.Branding.PrimaryColor);
            _brandBrush = new XSolidBrush(_brandColor);
            _brandPen = new XPen(_brandColor, 1.2);
        }

        public byte[] Render()
        {
            _document.Info.Title = $"RIDE {ValueOrDash(_ride.DocumentNumber)}";
            _document.Info.Subject = "Representacion impresa de comprobante electronico autorizado";
            _document.Info.Creator = "hfpos";

            NewPage(false);
            DrawHeader();
            DrawAccessKeyBlock();
            DrawBuyerBlock();
            DrawItems();
            DrawSummary();
            DrawRideFooter();
            _gfx.Dispose();
            DrawPageNumbers();

            using var stream = new MemoryStream();
            _document.Save(stream, false);
            return stream.ToArray();
        }

        private void NewPage(bool continuation)
        {
            if (_gfx is not null)
            {
                _gfx.Dispose();
            }

            _page = _document.AddPage();
            _page.Size = PageSize.A4;
            _page.Orientation = PageOrientation.Portrait;
            _gfx = XGraphics.FromPdfPage(_page);
            _text = new XTextFormatter(_gfx);
            _y = Margin;

            if (continuation)
            {
                DrawContinuationHeader();
            }
        }

        private double PageWidth => _page.Width.Point;

        private double PageHeight => _page.Height.Point;

        private double ContentWidth => PageWidth - (Margin * 2);

        private double BottomY => PageHeight - Margin - FooterReserve;

        private void DrawHeader()
        {
            var leftWidth = ContentWidth - 185 - Gap;
            var rightWidth = 185;
            var height = 146;
            var x = Margin;
            var rightX = x + leftWidth + Gap;

            DrawBox(x, _y, leftWidth, height, XBrushes.White, _borderPen);
            DrawBox(rightX, _y, rightWidth, height, XBrushes.White, _brandPen);

            DrawIssuerHeader(x + 8, _y + 8, leftWidth - 16, height - 16);
            DrawDocumentHeader(rightX + 8, _y + 8, rightWidth - 16, height - 16);

            _y += height + Gap;
        }

        private void DrawIssuerHeader(double x, double y, double width, double height)
        {
            var logoWidth = 104;
            var logoHeight = 43;
            var logoRect = new XRect(x, y, logoWidth, logoHeight);
            using var logo = TryCreateDataUrlImage(_ride.Branding.LogoDataUrl, _ride.Branding.LogoContentType);

            if (logo is not null)
            {
                DrawImageContained(logo, logoRect);
            }
            else
            {
                DrawLogoFallback(logoRect, IssuerInitials());
            }

            var identityX = x + logoWidth + 8;
            DrawKicker("Emisor", identityX, y, width - logoWidth - 8);
            DrawWrapped(ValueOrDash(_ride.Issuer.TradeName ?? _ride.Issuer.LegalName), _headerFont, _textBrush, identityX, y + 10, width - logoWidth - 8, 24);
            DrawWrapped(ValueOrDash(_ride.Issuer.LegalName), _bodyBoldFont, _mutedBrush, identityX, y + 36, width - logoWidth - 8, 16);

            _gfx.DrawLine(_brandPen, x, y + 52, x + width, y + 52);

            var rowY = y + 60;
            rowY = DrawField("RUC", _ride.Issuer.Ruc, x, rowY, 105);
            DrawField("Obligado contabilidad", _ride.Issuer.AccountingRequired, x + 116, y + 60, 120);
            if (!string.IsNullOrWhiteSpace(_ride.Issuer.TaxpayerRegime))
            {
                DrawField("Regimen", _ride.Issuer.TaxpayerRegime, x + 244, y + 60, width - 244);
            }

            rowY += 4;
            rowY = DrawWideField("Dir. matriz", _ride.Issuer.MatrixAddress, x, rowY, width);
            DrawWideField("Dir. establecimiento", _ride.Issuer.EstablishmentAddress, x, rowY, width);
        }

        private void DrawDocumentHeader(double x, double y, double width, double height)
        {
            DrawKicker("Comprobante electronico", x, y, width);
            _gfx.DrawString(ValueOrDash(_ride.DocumentTypeLabel).ToUpperInvariant(), _subHeaderFont, _brandBrush, new XRect(x, y + 12, width, 13), TopLeft);

            var numberRect = new XRect(x, y + 29, width, 24);
            DrawBox(numberRect.X, numberRect.Y, numberRect.Width, numberRect.Height, _softBrush, _brandPen);
            _gfx.DrawString(ValueOrDash(_ride.DocumentNumber), _headerFont, _textBrush, numberRect, Center);

            var rowY = y + 61;
            rowY = DrawCodeField("Autorizacion", _ride.AuthorizationNumber, x, rowY, width);
            rowY = DrawWideField("Fecha autorizacion", FormatDateTime(_ride.AuthorizationDate), x, rowY, width);
            rowY = DrawField("Ambiente", _ride.EnvironmentLabel, x, rowY, width / 2 - 4);
            DrawField("Emision", _ride.EmissionTypeLabel, x + width / 2 + 4, rowY - 18, width / 2 - 4);
        }

        private void DrawAccessKeyBlock()
        {
            var height = 73;
            EnsureSpace(height + Gap);
            DrawBox(Margin, _y, ContentWidth, height, _softBrush, _borderPen);

            var qrSize = 56;
            var qrX = Margin + ContentWidth - qrSize - 10;
            var textWidth = ContentWidth - qrSize - 24;
            DrawKicker("Clave de acceso / consulta SRI", Margin + 8, _y + 8, textWidth);
            DrawWrapped(ValueOrDash(_ride.AccessKey), _codeFont, _textBrush, Margin + 8, _y + 22, textWidth, 42);

            var qrContent = TrimToNull(_ride.Qr?.Content) ?? TrimToNull(_ride.AccessKey);
            if (qrContent is not null)
            {
                var qrRect = new XRect(qrX, _y + 8, qrSize, qrSize);
                DrawBox(qrRect.X - 3, qrRect.Y - 3, qrRect.Width + 6, qrRect.Height + 11, XBrushes.White, _softBorderPen);

                if (TryDrawQrCode(qrContent, qrRect))
                {
                    _gfx.DrawString("QR clave", _smallBoldFont, _mutedBrush, new XRect(qrX - 3, _y + 63, qrSize + 6, 8), Center);
                }
            }

            _y += height + Gap;
        }

        private void DrawBuyerBlock()
        {
            var buyerAddress = TrimToNull(_ride.Buyer.Address);
            var addressHeight = buyerAddress is null
                ? 0
                : Math.Max(12, EstimateWrappedHeight(buyerAddress, _bodyFont, ContentWidth - 16));
            var height = buyerAddress is null
                ? 42
                : Math.Max(60, 49 + addressHeight);
            EnsureSpace(height + Gap);
            DrawBox(Margin, _y, ContentWidth, height, XBrushes.White, _borderPen);

            var col1 = ContentWidth * 0.52;
            var col2 = ContentWidth * 0.25;
            var col3 = ContentWidth - col1 - col2;
            var x = Margin + 8;
            DrawField("Razon social / Nombres y apellidos", _ride.Buyer.LegalName, x, _y + 8, col1 - 12);
            DrawField("Identificacion", BuyerIdentification(), x + col1, _y + 8, col2 - 12);
            DrawField("Fecha emision", FormatDate(_ride.IssueDate), x + col1 + col2, _y + 8, col3 - 16);

            if (buyerAddress is not null)
            {
                DrawWideField("Direccion comprador", buyerAddress, x, _y + 31, ContentWidth - 16);
            }

            _y += height + Gap;
        }

        private void DrawItems()
        {
            DrawItemsHeader();

            if (_ride.Items.Count == 0)
            {
                EnsureSpaceForItemRow(20, true);
                DrawEmptyItemsRow();
                return;
            }

            for (var index = 0; index < _ride.Items.Count; index++)
            {
                var item = _ride.Items[index];
                var rowHeight = EstimateItemRowHeight(item);
                var reserve = index == _ride.Items.Count - 1 ? EstimateSummaryHeight() + 45 : 28;
                EnsureSpaceForItemRow(rowHeight, reserve > 80);
                DrawItemRow(item, rowHeight);
            }
        }

        private void DrawItemsHeader()
        {
            EnsureSpace(20);
            var columns = ItemColumns();
            var headers = new[] { "Codigo", "Descripcion", "Cant.", "P. Unit.", "Desc.", "Subtotal", "IVA", "Total" };
            var x = Margin;
            var headerHeight = 18;

            for (var index = 0; index < columns.Length; index++)
            {
                _gfx.DrawRectangle(_darkHeaderBrush, x, _y, columns[index], headerHeight);
                var format = index <= 1 ? TopLeft : TopRight;
                var offsetX = index <= 1 ? 3 : -3;
                _gfx.DrawString(headers[index], _smallBoldFont, XBrushes.White, new XRect(x + offsetX, _y + 5, columns[index] - 6, 10), format);
                x += columns[index];
            }

            _y += headerHeight;
        }

        private void DrawEmptyItemsRow()
        {
            DrawBox(Margin, _y, ContentWidth, 20, XBrushes.White, _borderPen);
            _gfx.DrawString("No hay items en el XML autorizado.", _bodyFont, _mutedBrush, new XRect(Margin, _y + 6, ContentWidth, 10), Center);
            _y += 20 + Gap;
        }

        private void DrawItemRow(SriRideItemDto item, double rowHeight)
        {
            var columns = ItemColumns();
            var x = Margin;
            var y = _y;
            var values = new[]
            {
                ValueOrDash(item.MainCode),
                ValueOrDash(item.Description),
                item.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
                FormatMoney(item.UnitPrice),
                FormatMoney(item.Discount),
                FormatMoney(item.Subtotal),
                FormatMoney(item.TaxAmount),
                FormatMoney(item.LineTotal)
            };

            DrawBox(Margin, y, ContentWidth, rowHeight, XBrushes.White, _softBorderPen);

            for (var index = 0; index < columns.Length; index++)
            {
                var rect = new XRect(x + 3, y + 4, columns[index] - 6, rowHeight - 6);
                if (index <= 1)
                {
                    DrawWrapped(values[index], index == 1 ? _bodyFont : _smallFont, _textBrush, rect.X, rect.Y, rect.Width, rect.Height);
                }
                else
                {
                    _gfx.DrawString(values[index], _bodyFont, _textBrush, rect, TopRight);
                }

                if (index < columns.Length - 1)
                {
                    _gfx.DrawLine(_softBorderPen, x + columns[index], y, x + columns[index], y + rowHeight);
                }

                x += columns[index];
            }

            _y += rowHeight;
        }

        private double[] ItemColumns()
            => new[] { 48d, 174d, 37d, 54d, 47d, 56d, 45d, ContentWidth - 48d - 174d - 37d - 54d - 47d - 56d - 45d };

        private double EstimateItemRowHeight(SriRideItemDto item)
        {
            var descriptionWidth = ItemColumns()[1] - 6;
            var descriptionHeight = EstimateWrappedHeight(ValueOrDash(item.Description), _bodyFont, descriptionWidth);

            return Math.Max(22, Math.Min(descriptionHeight + 9, 56));
        }

        private void EnsureSpaceForItemRow(double rowHeight, bool reserveSummary)
        {
            var reserve = reserveSummary ? EstimateSummaryHeight() + 45 : 28;
            if (_y + rowHeight + reserve <= BottomY)
            {
                return;
            }

            NewPage(true);
            DrawItemsHeader();
        }

        private void DrawSummary()
        {
            var estimatedHeight = EstimateSummaryHeight();
            EnsureSpace(estimatedHeight + 42);

            var leftWidth = ContentWidth - 190 - Gap;
            var rightWidth = 190;
            var leftX = Margin;
            var rightX = leftX + leftWidth + Gap;
            var startY = _y;

            var leftHeight = DrawLowerLeft(leftX, startY, leftWidth);
            var totalsHeight = DrawTotals(rightX, startY, rightWidth);
            _y = startY + Math.Max(leftHeight, totalsHeight) + Gap;
        }

        private double DrawLowerLeft(double x, double y, double width)
        {
            var cursor = y;

            if (_ride.AdditionalInfo.Count > 0)
            {
                var infoHeight = 24 + Math.Min(_ride.AdditionalInfo.Count, 8) * 15;
                DrawBox(x, cursor, width, infoHeight, XBrushes.White, _borderPen);
                _gfx.DrawString("Informacion adicional", _subHeaderFont, _brandBrush, new XRect(x + 8, cursor + 7, width - 16, 11), TopLeft);
                cursor += 22;

                foreach (var field in _ride.AdditionalInfo.Take(8))
                {
                    DrawTwoPartLine(field.Name, field.Value, x + 8, cursor, width - 16);
                    cursor += 15;
                }

                cursor = y + infoHeight + Gap;
            }

            var paymentHeight = 24 + Math.Max(_ride.Payments.Count, 1) * 15;
            DrawBox(x, cursor, width, paymentHeight, XBrushes.White, _borderPen);
            _gfx.DrawString("Forma de pago", _subHeaderFont, _brandBrush, new XRect(x + 8, cursor + 7, width - 16, 11), TopLeft);
            cursor += 22;

            if (_ride.Payments.Count == 0)
            {
                _gfx.DrawString("Sin pagos informados.", _bodyFont, _mutedBrush, new XRect(x + 8, cursor, width - 16, 10), TopLeft);
            }
            else
            {
                foreach (var payment in _ride.Payments)
                {
                    DrawTwoPartLine(payment.PaymentMethod ?? "-", FormatMoney(payment.Amount), x + 8, cursor, width - 16);
                    cursor += 15;
                }
            }

            return cursor - y + 8;
        }

        private double DrawTotals(double x, double y, double width)
        {
            var totals = BuildTotals();
            var height = 14 + totals.Count * 15 + 9;
            DrawBox(x, y, width, height, _softBrush, _borderPen);
            var cursor = y + 8;

            foreach (var total in totals)
            {
                var isFinal = total.Label == "Total";
                var font = isFinal ? _subHeaderFont : _bodyBoldFont;
                var labelFont = isFinal ? _subHeaderFont : _bodyFont;

                if (isFinal)
                {
                    _gfx.DrawLine(_brandPen, x + 8, cursor - 3, x + width - 8, cursor - 3);
                }

                _gfx.DrawString(total.Label, labelFont, _textBrush, new XRect(x + 8, cursor, width - 80, 11), TopLeft);
                _gfx.DrawString(total.Value, font, _textBrush, new XRect(x + width - 86, cursor, 78, 11), TopRight);
                cursor += isFinal ? 17 : 15;
            }

            return height;
        }

        private IReadOnlyList<(string Label, string Value)> BuildTotals()
        {
            var totals = new List<(string Label, string Value)>();

            if (_ride.Totals.Vat15Subtotal > 0)
            {
                totals.Add(("Subtotal IVA 15%", FormatMoney(_ride.Totals.Vat15Subtotal)));
            }

            if (_ride.Totals.Vat5Subtotal > 0)
            {
                totals.Add(("Subtotal IVA 5%", FormatMoney(_ride.Totals.Vat5Subtotal)));
            }

            if (_ride.Totals.Vat0Subtotal > 0)
            {
                totals.Add(("Subtotal IVA 0%", FormatMoney(_ride.Totals.Vat0Subtotal)));
            }

            if (_ride.Totals.ExemptSubtotal > 0)
            {
                totals.Add(("Subtotal exento", FormatMoney(_ride.Totals.ExemptSubtotal)));
            }

            if (_ride.Totals.NotSubjectSubtotal > 0)
            {
                totals.Add(("Subtotal no objeto", FormatMoney(_ride.Totals.NotSubjectSubtotal)));
            }

            totals.Add(("Subtotal sin impuestos", FormatMoney(_ride.Totals.SubtotalWithoutTaxes)));
            totals.Add(("Descuento", FormatMoney(_ride.Totals.TotalDiscount)));
            totals.Add(("IVA", FormatMoney(_ride.Totals.TaxAmount)));
            totals.Add(("Total", FormatMoney(_ride.Totals.Total)));

            return totals;
        }

        private double EstimateSummaryHeight()
        {
            var additionalRows = _ride.AdditionalInfo.Count > 0 ? Math.Min(_ride.AdditionalInfo.Count, 8) : 0;
            var leftHeight = (_ride.AdditionalInfo.Count > 0 ? 24 + additionalRows * 15 + Gap : 0)
                + 24 + Math.Max(_ride.Payments.Count, 1) * 15 + 8;
            var totalsHeight = 14 + BuildTotals().Count * 15 + 9;

            return Math.Max(leftHeight, totalsHeight);
        }

        private void DrawRideFooter()
        {
            var customFooter = TrimToNull(_ride.FooterNote);
            var footerText = customFooter ?? DefaultFooterNote;
            var hasCustomFooter = !string.Equals(customFooter, DefaultFooterNote, StringComparison.Ordinal);
            var innerWidth = ContentWidth - 16;
            var footerLines = WrapTextByWidth(footerText, _smallBoldFont, innerWidth);
            var defaultLines = hasCustomFooter
                ? WrapTextByWidth(DefaultFooterNote, _smallFont, innerWidth)
                : Array.Empty<string>();
            var footerHeight = LinesHeight(footerLines, _smallBoldFont);
            var defaultHeight = hasCustomFooter ? LinesHeight(defaultLines, _smallFont) : 0;
            var height = Math.Max(24, 14 + footerHeight + (hasCustomFooter ? 4 + defaultHeight : 0));

            EnsureSpace(height);
            DrawBox(Margin, _y, ContentWidth, height, XBrushes.White, _borderPen);
            var cursor = _y + 7;
            DrawWrappedLines(footerLines, _smallBoldFont, _mutedBrush, Margin + 8, cursor, innerWidth, Center);

            if (hasCustomFooter)
            {
                cursor += footerHeight + 4;
                DrawWrappedLines(defaultLines, _smallFont, _mutedBrush, Margin + 8, cursor, innerWidth, Center);
            }

            _y += height;
        }

        private void DrawContinuationHeader()
        {
            var height = 28;
            DrawBox(Margin, _y, ContentWidth, height, XBrushes.White, _borderPen);
            _gfx.DrawString("RIDE", _subHeaderFont, _brandBrush, new XRect(Margin + 8, _y + 8, 50, 12), TopLeft);
            _gfx.DrawString(ValueOrDash(_ride.DocumentNumber), _bodyBoldFont, _textBrush, new XRect(Margin + 62, _y + 8, 150, 12), TopLeft);
            _gfx.DrawString("Continuacion de detalle", _smallBoldFont, _mutedBrush, new XRect(Margin + 220, _y + 9, ContentWidth - 228, 10), TopRight);
            _y += height + Gap;
        }

        private void DrawPageNumbers()
        {
            if (_document.PageCount <= 1)
            {
                return;
            }

            for (var index = 0; index < _document.PageCount; index++)
            {
                using var graphics = XGraphics.FromPdfPage(_document.Pages[index], XGraphicsPdfPageOptions.Append);
                graphics.DrawString(
                    $"Pagina {index + 1} de {_document.PageCount}",
                    _smallFont,
                    _mutedBrush,
                    new XRect(Margin, PageHeight - Margin + 5, ContentWidth, 10),
                    TopRight);
            }
        }

        private void EnsureSpace(double height)
        {
            if (_y + height <= BottomY)
            {
                return;
            }

            NewPage(true);
        }

        private void DrawBox(double x, double y, double width, double height, XBrush brush, XPen pen)
        {
            _gfx.DrawRectangle(brush, x, y, width, height);
            _gfx.DrawRectangle(pen, x, y, width, height);
        }

        private void DrawKicker(string label, double x, double y, double width)
            => _gfx.DrawString(label.ToUpperInvariant(), _smallBoldFont, _mutedBrush, new XRect(x, y, width, 8), TopLeft);

        private double DrawField(string label, string? value, double x, double y, double width)
        {
            DrawKicker(label, x, y, width);
            DrawWrapped(ValueOrDash(value), _bodyBoldFont, _textBrush, x, y + 10, width, 12);

            return y + 18;
        }

        private double DrawWideField(string label, string? value, double x, double y, double width)
        {
            DrawKicker(label, x, y, width);
            var height = Math.Max(12, EstimateWrappedHeight(ValueOrDash(value), _bodyFont, width));
            DrawWrapped(ValueOrDash(value), _bodyFont, _textBrush, x, y + 10, width, height);

            return y + 12 + height;
        }

        private double DrawCodeField(string label, string? value, double x, double y, double width)
        {
            DrawKicker(label, x, y, width);
            var lines = WrapIdentifierByWidth(ValueOrDash(value), _compactCodeFont, width);
            var height = Math.Max(12, LinesHeight(lines, _compactCodeFont));
            DrawWrappedLines(lines, _compactCodeFont, _textBrush, x, y + 10, width, TopLeft);

            return y + 12 + height;
        }

        private void DrawTwoPartLine(string? label, string? value, double x, double y, double width)
        {
            _gfx.DrawString(ValueOrDash(label), _smallBoldFont, _mutedBrush, new XRect(x, y, width * 0.36, 10), TopLeft);
            _gfx.DrawString(ValueOrDash(value), _bodyFont, _textBrush, new XRect(x + width * 0.37, y, width * 0.63, 10), TopRight);
        }

        private void DrawWrapped(string text, XFont font, XBrush brush, double x, double y, double width, double height)
            => _text.DrawString(ValueOrDash(text), font, brush, new XRect(x, y, width, height), TopLeft);

        private double DrawWrappedLines(
            IReadOnlyList<string> lines,
            XFont font,
            XBrush brush,
            double x,
            double y,
            double width,
            XStringFormat format)
        {
            var lineHeight = LineHeight(font);

            for (var index = 0; index < lines.Count; index++)
            {
                _gfx.DrawString(lines[index], font, brush, new XRect(x, y + index * lineHeight, width, lineHeight), format);
            }

            return lines.Count * lineHeight;
        }

        private void DrawLogoFallback(XRect rect, string initials)
        {
            var size = Math.Min(rect.Width, rect.Height);
            var square = new XRect(rect.X, rect.Y, size, size);
            DrawBox(square.X, square.Y, square.Width, square.Height, XBrushes.White, _brandPen);
            _gfx.DrawString(initials, _headerFont, _brandBrush, square, Center);
        }

        private void DrawImageContained(XImage image, XRect rect)
        {
            var width = image.PixelWidth > 0 ? image.PixelWidth : image.PointWidth;
            var height = image.PixelHeight > 0 ? image.PixelHeight : image.PointHeight;
            var scale = Math.Min(rect.Width / width, rect.Height / height);
            var drawWidth = width * scale;
            var drawHeight = height * scale;
            var drawX = rect.X + (rect.Width - drawWidth) / 2;
            var drawY = rect.Y + (rect.Height - drawHeight) / 2;

            _gfx.DrawImage(image, drawX, drawY, drawWidth, drawHeight);
        }

        private bool TryDrawQrCode(string content, XRect rect)
        {
            try
            {
                using var generator = new QRCodeGenerator();
                using var qrData = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
                var modules = qrData.ModuleMatrix;
                var moduleCount = modules.Count;

                if (moduleCount == 0)
                {
                    return false;
                }

                const int quietZoneModules = 4;
                var moduleSize = Math.Min(rect.Width, rect.Height) / (moduleCount + quietZoneModules * 2);
                var drawSize = moduleSize * moduleCount;
                var drawX = rect.X + (rect.Width - drawSize) / 2;
                var drawY = rect.Y + (rect.Height - drawSize) / 2;

                _gfx.DrawRectangle(XBrushes.White, rect);

                for (var row = 0; row < moduleCount; row++)
                {
                    for (var column = 0; column < moduleCount; column++)
                    {
                        if (!modules[row][column])
                        {
                            continue;
                        }

                        _gfx.DrawRectangle(
                            XBrushes.Black,
                            drawX + column * moduleSize,
                            drawY + row * moduleSize,
                            moduleSize + 0.05,
                            moduleSize + 0.05);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static XImage? TryCreateDataUrlImage(string? dataUrl, string? contentType)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                return null;
            }

            try
            {
                var commaIndex = dataUrl.IndexOf(',');

                if (commaIndex < 0)
                {
                    return null;
                }

                var metadata = dataUrl[..commaIndex];
                var normalizedContentType = TrimToNull(contentType)?.Split(';', 2)[0].Trim().ToLowerInvariant()
                    ?? metadata.Replace("data:", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Split(';', 2)[0]
                        .Trim()
                        .ToLowerInvariant();

                if (normalizedContentType is not ("image/png" or "image/jpeg" or "image/jpg"))
                {
                    return null;
                }

                if (!metadata.Contains("base64", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var bytes = Convert.FromBase64String(dataUrl[(commaIndex + 1)..]);
                return XImage.FromStream(new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }

        private string BuyerIdentification()
        {
            var type = TrimToNull(_ride.Buyer.IdentificationType);
            var identification = TrimToNull(_ride.Buyer.Identification);

            if (type is null && identification is null)
            {
                return "-";
            }

            if (type is null || type == identification)
            {
                return identification ?? "-";
            }

            if (IsFinalConsumerIdentificationType(type))
            {
                return identification ?? "-";
            }

            return identification is null ? type : $"{type}: {identification}";
        }

        private string IssuerInitials()
        {
            var name = TrimToNull(_ride.Issuer.TradeName) ?? TrimToNull(_ride.Issuer.LegalName) ?? "HFPOS";
            var initials = string.Concat(name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));

            return string.IsNullOrWhiteSpace(initials) ? "HF" : initials;
        }

        private string FormatMoney(decimal value)
        {
            var amount = value.ToString("#,##0.00", MoneyCulture);
            var currency = CurrencySymbolOrLabel(_ride.Totals.Currency);
            var separator = currency == "$" ? string.Empty : " ";

            return $"{currency}{separator}{amount}";
        }

        private static string CurrencySymbolOrLabel(string? currency)
        {
            var normalized = (TrimToNull(currency) ?? "USD").ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var compact = new string(normalized
                .Where(character => (character >= 'A' && character <= 'Z') || character == '$')
                .ToArray());

            return compact is "$" or "USD" or "DOLAR" or "DOLARES" or "DOLLAR" or "DOLLARS" or "USDOLLAR" or "USDOLLARS"
                ? "$"
                : compact;
        }

        private static string FormatDate(DateTime? value)
            => value?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-";

        private static string FormatDateTime(DateTime? value)
            => value?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-";

        private static string ValueOrDash(string? value)
            => TrimToNull(value) ?? "-";

        private static double EstimateWrappedHeight(string text, XFont font, double width)
        {
            var normalized = ValueOrDash(text);
            var charsPerLine = Math.Max(8, (int)Math.Floor(width / Math.Max(font.Size * 0.48, 1)));
            var lines = normalized
                .Split('\n')
                .Select(line => Math.Max(1, (int)Math.Ceiling((double)line.Length / charsPerLine)))
                .Sum();

            return lines * font.Size * 1.22;
        }

        private IReadOnlyList<string> WrapTextByWidth(string text, XFont font, double width)
        {
            var lines = new List<string>();
            var paragraphs = ValueOrDash(text)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            foreach (var paragraph in paragraphs)
            {
                var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (words.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var current = string.Empty;

                foreach (var word in words)
                {
                    var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";

                    if (TextFits(candidate, font, width))
                    {
                        current = candidate;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(current))
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }

                    var chunks = SplitTokenByWidth(word, font, width);
                    for (var index = 0; index < chunks.Count - 1; index++)
                    {
                        lines.Add(chunks[index]);
                    }

                    current = chunks[^1];
                }

                if (!string.IsNullOrEmpty(current))
                {
                    lines.Add(current);
                }
            }

            return lines.Count == 0 ? new[] { "-" } : lines;
        }

        private IReadOnlyList<string> WrapIdentifierByWidth(string text, XFont font, double width)
        {
            var normalized = ValueOrDash(text);

            if (normalized.Any(char.IsWhiteSpace))
            {
                return WrapTextByWidth(normalized, font, width);
            }

            var maxLength = FittingPrefixLength(normalized, font, width);
            if (maxLength >= normalized.Length)
            {
                return new[] { normalized };
            }

            var lineCount = (int)Math.Ceiling((double)normalized.Length / maxLength);
            var targetLength = (int)Math.Ceiling((double)normalized.Length / lineCount);

            while (targetLength > 1 && !TextFits(normalized[..targetLength], font, width))
            {
                targetLength--;
            }

            var lines = new List<string>();
            for (var index = 0; index < normalized.Length; index += targetLength)
            {
                lines.Add(normalized.Substring(index, Math.Min(targetLength, normalized.Length - index)));
            }

            return lines;
        }

        private IReadOnlyList<string> SplitTokenByWidth(string token, XFont font, double width)
        {
            var chunks = new List<string>();
            var remaining = token;

            while (remaining.Length > 0)
            {
                var length = FittingPrefixLength(remaining, font, width);
                chunks.Add(remaining[..length]);
                remaining = remaining[length..];
            }

            return chunks;
        }

        private int FittingPrefixLength(string text, XFont font, double width)
        {
            var low = 1;
            var high = text.Length;
            var best = 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (TextFits(text[..middle], font, width))
                {
                    best = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return best;
        }

        private bool TextFits(string text, XFont font, double width)
            => _gfx.MeasureString(text, font).Width <= width;

        private static double LinesHeight(IReadOnlyCollection<string> lines, XFont font)
            => lines.Count * LineHeight(font);

        private static double LineHeight(XFont font)
            => font.Size * 1.28;

        private static bool IsFinalConsumerIdentificationType(string value)
        {
            if (string.Equals(TrimToNull(value), "07", StringComparison.Ordinal))
            {
                return true;
            }

            var normalized = value.ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var compact = new string(normalized
                .Where(character => character >= 'A' && character <= 'Z')
                .ToArray());

            return compact is "CONSUMIDORFINAL" or "CONSUMIDORFIN";
        }

        private static XColor ParseBrandColor(string? value)
        {
            var normalized = TrimToNull(value);

            if (normalized is null)
            {
                return XColor.FromArgb(29, 78, 216);
            }

            if (normalized.StartsWith('#'))
            {
                normalized = normalized[1..];
            }

            if (normalized.Length != 6
                || !int.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
                || !int.TryParse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
                || !int.TryParse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            {
                return XColor.FromArgb(29, 78, 216);
            }

            return XColor.FromArgb(red, green, blue);
        }
    }
}
