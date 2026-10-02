using System.Text;
using Microsoft.AspNetCore.Authorization;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Unit;

public sealed class PurchaseReceiptCsvExportBuilderTests
{
    [Fact]
    public void Empty_export_has_bom_and_exact_headers()
    {
        var bytes = PurchaseReceiptCsvExportBuilder.Build([]);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        Assert.Equal("\uFEFFID;Fecha negocio;Proveedor;Numero recepcion;Documento proveedor;Estado;Subtotal;Creado por;Fecha creacion;Fecha cancelacion;Cancelado por;Motivo cancelacion",
            Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1)")]
    [InlineData(" \t=1+1")]
    [InlineData("\r\n+1+1")]
    [InlineData("\t-1+1")]
    [InlineData("\u0001@SUM(1)")]
    public void All_user_text_is_formula_neutralized_before_quoting(string input)
    {
        var row = Row();
        row.SupplierName = row.ReceiptNumber = row.SupplierDocumentNumber = input;
        row.CreatedByUsername = row.CanceledByUsername = row.CancelReason = input;
        var csv = Encoding.UTF8.GetString(PurchaseReceiptCsvExportBuilder.Build([row]));
        Assert.Equal(6, csv.Split("'" + input).Length - 1);
    }

    [Fact]
    public void Escapes_delimiter_quotes_and_newlines_and_uses_comma_decimals()
    {
        var row = Row();
        row.SupplierName = "Proveedor; \"especial\"\r\nsegunda linea";
        row.Subtotal = 123.45m;
        var csv = Encoding.UTF8.GetString(PurchaseReceiptCsvExportBuilder.Build([row]));
        Assert.Contains("\r\n1;", csv);
        Assert.Contains("\"Proveedor; \"\"especial\"\"\r\nsegunda linea\"", csv);
        Assert.Contains(";123,45;", csv);
        Assert.DoesNotContain('$', csv);
    }

    [Fact]
    public void Business_dates_and_each_instant_use_their_own_snapshots()
    {
        var row = Row();
        row.ReceiptBusinessDate = new DateOnly(2026, 9, 10);
        row.CreatedAt = new DateTime(2026, 9, 12, 2, 0, 0, DateTimeKind.Utc);
        row.Status = PurchaseReceiptStatus.Canceled;
        row.CanceledAt = new DateTime(2026, 9, 13, 2, 0, 0, DateTimeKind.Utc);
        row.CanceledBusinessDate = new DateOnly(2026, 9, 13);
        row.CanceledTimeZoneIdSnapshot = "Europe/Madrid";
        row.CanceledByUsername = "cancel-user";
        row.CancelReason = "reason";
        var csv = Encoding.UTF8.GetString(PurchaseReceiptCsvExportBuilder.Build([row]));
        Assert.Contains(";10/09/2026;", csv);
        Assert.Contains(";Cancelada;", csv);
        Assert.Contains(";11/09/2026 21:00;13/09/2026 04:00;cancel-user;reason", csv);
    }

    [Fact]
    public void Posted_receipt_has_empty_cancellation_columns()
    {
        var csv = Encoding.UTF8.GetString(PurchaseReceiptCsvExportBuilder.Build([Row()]));
        Assert.Contains(";Publicada;", csv);
        Assert.EndsWith(";;;", csv);
    }

    [Fact]
    public void Export_requires_the_existing_purchases_read_permission()
    {
        var action = typeof(PurchaseReceiptsController).GetMethod(nameof(PurchaseReceiptsController.Export))!;
        var permission = Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(AppPermissions.PurchasesRead, permission.Policy);
    }

    private static PurchaseReceiptCsvRowDto Row() => new()
    {
        Id = 1,
        ReceiptBusinessDate = new DateOnly(2026, 9, 12),
        ReceiptTimeZoneIdSnapshot = "America/Guayaquil",
        SupplierName = "Proveedor",
        CreatedByUsername = "user",
        CreatedAt = new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc),
        Status = PurchaseReceiptStatus.Posted
    };
}
