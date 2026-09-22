using System.Globalization;
using System.Text;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class SaleCsvExportBuilder
{
    private static readonly string[] Headers =
    [
        "ID",
        "Fecha",
        "Documento",
        "Cliente",
        "Identificación cliente",
        "Email cliente",
        "Tipo documento",
        "Estado venta",
        "Estado fiscal",
        "Total original",
        "Notas de crédito autorizadas",
        "Cantidad NC",
        "Total neto",
        "Costo original",
        "Costo revertido",
        "Costo neto",
        "Utilidad original",
        "Margen bruto %",
        "Utilidad neta",
        "Margen neto %",
        "Usuario",
        "Notas"
    ];

    public static byte[] Build(IReadOnlyList<SaleListItemDto> sales, TimeZoneInfo timeZone)
    {
        var lines = new List<string>(sales.Count + 1)
        {
            JoinRow(Headers)
        };

        lines.AddRange(sales.Select(sale => JoinRow(
        [
            sale.Id.ToString(CultureInfo.InvariantCulture),
            FormatDateTime(sale.CreatedAt, timeZone),
            sale.Number ?? string.Empty,
            sale.CustomerName ?? string.Empty,
            sale.CustomerIdentification ?? string.Empty,
            sale.CustomerEmail ?? string.Empty,
            sale.DocumentType == SaleDocumentType.Invoice ? "Factura" : "Ticket",
            SaleStatusLabel(sale.Status),
            FiscalStatusLabel(sale),
            FormatDecimal(sale.Total),
            FormatDecimal(sale.CreditNoteImpact.AuthorizedCreditNoteTotal),
            sale.CreditNoteImpact.AuthorizedCreditNoteCount.ToString(CultureInfo.InvariantCulture),
            FormatDecimal(sale.CreditNoteImpact.NetTotal),
            FormatDecimal(sale.TotalCost),
            FormatDecimal(sale.CreditNoteImpact.ReturnedCost),
            FormatDecimal(sale.CreditNoteImpact.NetCost),
            FormatDecimal(sale.GrossProfit),
            FormatDecimal(sale.GrossMarginPercent),
            FormatDecimal(sale.CreditNoteImpact.NetGrossProfit),
            FormatDecimal(sale.CreditNoteImpact.NetGrossMarginPercent),
            sale.Username,
            sale.Notes ?? string.Empty
        ])));

        var content = Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
        return result;
    }

    private static string JoinRow(IEnumerable<string> values)
        => string.Join(';', values.Select(Escape));

    private static string Escape(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return escaped.IndexOfAny(['\"', ';', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }

    private static string FormatDecimal(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string FormatDateTime(DateTime value, TimeZoneInfo timeZone)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static string SaleStatusLabel(SaleStatus status)
        => status switch
        {
            SaleStatus.Draft => "Borrador",
            SaleStatus.Voided => "Anulada",
            _ => "Completada"
        };

    private static string FiscalStatusLabel(SaleListItemDto sale)
    {
        if (sale.DocumentType != SaleDocumentType.Invoice)
        {
            return "No aplica";
        }

        if (sale.DocumentStatus == SaleDocumentStatus.Authorized
            || string.Equals(sale.SriAuthorizationStatus?.Trim(), "AUTORIZADO", StringComparison.OrdinalIgnoreCase))
        {
            return "Autorizado SRI";
        }

        return sale.DocumentStatus switch
        {
            SaleDocumentStatus.Draft => "Borrador",
            SaleDocumentStatus.PendingAuthorization => "Pendiente autorización",
            SaleDocumentStatus.Rejected => "Rechazado",
            SaleDocumentStatus.Cancelled => "Cancelado",
            _ => "No requerido"
        };
    }
}
