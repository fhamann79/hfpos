using System.Globalization;
using System.Text;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class PurchaseReceiptCsvExportBuilder
{
    public static byte[] Build(IReadOnlyList<PurchaseReceiptCsvRowDto> receipts)
    {
        var lines = new List<string>(receipts.Count + 1)
        {
            "ID;Fecha negocio;Proveedor;Numero recepcion;Documento proveedor;Estado;Subtotal;Creado por;Fecha creacion;Fecha cancelacion;Cancelado por;Motivo cancelacion"
        };
        lines.AddRange(receipts.Select(r => string.Join(';', new[]
        {
            r.Id.ToString(CultureInfo.InvariantCulture),
            r.ReceiptBusinessDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            SafeText(r.SupplierName),
            SafeText(r.ReceiptNumber),
            SafeText(r.SupplierDocumentNumber),
            r.Status == PurchaseReceiptStatus.Canceled ? "Cancelada" : "Publicada",
            r.Subtotal.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ','),
            SafeText(r.CreatedByUsername),
            FormatInstant(r.CreatedAt, r.ReceiptTimeZoneIdSnapshot),
            r.CanceledAt.HasValue
                ? FormatInstant(r.CanceledAt.Value,
                    r.CanceledTimeZoneIdSnapshot ?? r.ReceiptTimeZoneIdSnapshot, r.CanceledBusinessDate)
                : string.Empty,
            SafeText(r.CanceledByUsername),
            SafeText(r.CancelReason)
        }.Select(Escape))));

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(string.Join("\r\n", lines))).ToArray();
    }

    private static string SafeText(string? value)
    {
        value ??= string.Empty;
        // Whitespace/control prefixes must not hide a spreadsheet formula marker.
        var first = value.FirstOrDefault(c => !char.IsWhiteSpace(c) && !char.IsControl(c));
        return first is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    private static string Escape(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny(['\"', ';', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }

    private static string FormatInstant(DateTime value, string zoneId, DateOnly? businessDate = null)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, new BusinessClockService().ResolveTimeZone(zoneId));
        var date = businessDate ?? DateOnly.FromDateTime(local);
        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            + local.ToString(" HH:mm", CultureInfo.InvariantCulture);
    }
}
