using System.Text;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class SaleCsvExportBuilderTests
{
    // Exercise the internal output builder without widening its production visibility.
    private static readonly Func<IReadOnlyList<SaleListItemDto>, TimeZoneInfo, byte[]> Build =
        typeof(SalesService).Assembly
            .GetType("Pos.Backend.Api.Infrastructure.Services.SaleCsvExportBuilder", throwOnError: true)!
            .GetMethod("Build")!
            .CreateDelegate<Func<IReadOnlyList<SaleListItemDto>, TimeZoneInfo, byte[]>>();

    [Fact]
    public void Empty_export_has_utf8_bom_and_exact_column_order()
    {
        var bytes = Build([], TimeZoneInfo.Utc);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        Assert.Equal("\uFEFFID;Fecha;Documento;Cliente;Identificaci\u00f3n cliente;Email cliente;"
            + "Tipo documento;Estado venta;Estado fiscal;Total original;Notas de cr\u00e9dito autorizadas;"
            + "Cantidad NC;Total neto;Costo original;Costo revertido;Costo neto;Utilidad original;"
            + "Margen bruto %;Utilidad neta;Margen neto %;Usuario;Notas", Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-1+1", "'-1+1")]
    [InlineData("@SUM(1)", "'@SUM(1)")]
    [InlineData(" \t=1+1", "' \t=1+1")]
    [InlineData("\r\n+1+1", "\"'\r\n+1+1\"")]
    [InlineData("\t-1+1", "'\t-1+1")]
    [InlineData("\u0001@SUM(1)", "'\u0001@SUM(1)")]
    [InlineData(" \t=SUM(\"1\";2)\r\nline", "\"' \t=SUM(\"\"1\"\";2)\r\nline\"")]
    public void All_six_user_fields_are_neutralized_before_csv_escaping(string input, string expectedCell)
        => AssertUserText(input, expectedCell);

    public static IEnumerable<object[]> HiddenFormulaPrefixes()
    {
        string[] prefixes = [" ", "\t", "\r", "\n", "\u0000", "\u0001", "\u007f", "\u00a0", " \t\r\n\u0001"];
        foreach (var prefix in prefixes)
        {
            foreach (var marker in new[] { '=', '+', '-', '@' })
            {
                var input = prefix + marker + "1";
                var expectedCell = "'" + input;
                if (prefix.Contains('\r') || prefix.Contains('\n'))
                {
                    expectedCell = "\"" + expectedCell + "\"";
                }
                yield return [input, expectedCell];
            }
        }
    }

    [Theory]
    [MemberData(nameof(HiddenFormulaPrefixes))]
    public void Whitespace_and_control_prefixes_cannot_hide_any_formula_marker(string input, string expectedCell)
        => AssertUserText(input, expectedCell);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" \t\u0001", " \t\u0001")]
    [InlineData("\r\n", "\"\r\n\"")]
    [InlineData("Normal text", "Normal text")]
    [InlineData("Jos\u00e9", "Jos\u00e9")]
    [InlineData("normal@example.test", "normal@example.test")]
    [InlineData("TEXT=+1-@", "TEXT=+1-@")]
    [InlineData("'@SUM(1)", "'@SUM(1)")]
    [InlineData(" \tNormal", " \tNormal")]
    [InlineData("Cliente; \"especial\"\r\nsegunda linea", "\"Cliente; \"\"especial\"\"\r\nsegunda linea\"")]
    public void Ordinary_null_empty_and_escaped_text_is_preserved(string? input, string expectedCell)
        => AssertUserText(input, expectedCell);

    [Fact]
    public void Negative_amounts_and_percentages_remain_numeric_with_two_comma_decimals()
    {
        var sale = Row();
        sale.Total = -1234.5m;
        sale.TotalCost = -10m;
        sale.GrossProfit = -1224.5m;
        sale.GrossMarginPercent = -99.25m;
        sale.CreditNoteImpact = new SaleCreditNoteImpactDto
        {
            AuthorizedCreditNoteTotal = -0.5m,
            AuthorizedCreditNoteCount = 2,
            NetTotal = -1234m,
            ReturnedCost = -1m,
            NetCost = -9m,
            NetGrossProfit = -1225m,
            NetGrossMarginPercent = -99.27m
        };

        Assert.Equal("1;18/09/2026 15:00;DOC-001;Cliente;TEST-ID;buyer@example.test;Ticket;"
            + "Completada;No aplica;-1234,50;-0,50;2;-1234,00;-10,00;-1,00;-9,00;-1224,50;"
            + "-99,25;-1225,00;-99,27;user;Notes", ExportRow(sale));
    }

    private static void AssertUserText(string? input, string expectedCell)
    {
        var sale = Row();
        sale.Number = sale.CustomerName = sale.CustomerIdentification = sale.CustomerEmail = sale.Notes = input;
        sale.Username = input!;

        Assert.Equal(string.Join(';', new[]
        {
            "1", "18/09/2026 15:00", expectedCell, expectedCell, expectedCell, expectedCell,
            "Ticket", "Completada", "No aplica", "0,00", "0,00", "0", "0,00", "0,00", "0,00",
            "0,00", "0,00", "0,00", "0,00", "0,00", expectedCell, expectedCell
        }), ExportRow(sale));

        Assert.Equal(input, sale.Number);
        Assert.Equal(input, sale.CustomerName);
        Assert.Equal(input, sale.CustomerIdentification);
        Assert.Equal(input, sale.CustomerEmail);
        Assert.Equal(input, sale.Username);
        Assert.Equal(input, sale.Notes);
    }

    private static string ExportRow(SaleListItemDto sale)
    {
        var csv = Encoding.UTF8.GetString(Build([sale], TimeZoneInfo.Utc));
        return csv[(csv.IndexOf("\r\n", StringComparison.Ordinal) + 2)..];
    }

    private static SaleListItemDto Row() => new()
    {
        Id = 1,
        CreatedAt = new DateTime(2026, 9, 18, 15, 0, 0, DateTimeKind.Utc),
        Number = "DOC-001",
        CustomerName = "Cliente",
        CustomerIdentification = "TEST-ID",
        CustomerEmail = "buyer@example.test",
        Username = "user",
        Notes = "Notes",
        Status = SaleStatus.Completed,
        DocumentType = SaleDocumentType.Ticket
    };
}
