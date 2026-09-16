using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

internal static class CreditNoteReporting
{
    // Both reports share fiscal recognition and historical cost, never financial refunds.
    private static IQueryable<NoteProjection> AuthorizedInContext(
        PosDbContext context, OperationalContext scope)
    {
        return context.CreditNotes.AsNoTracking()
            .Where(n => n.CompanyId == scope.CompanyId
                && n.EstablishmentId == scope.EstablishmentId
                && n.EmissionPointId == scope.EmissionPointId
                && n.OriginalSale.CompanyId == scope.CompanyId
                && n.OriginalSale.EstablishmentId == scope.EstablishmentId
                && n.OriginalSale.EmissionPointId == scope.EmissionPointId
                && n.VoidedAt == null
                && n.DocumentStatus != SaleDocumentStatus.Cancelled
                && n.DocumentStatus != SaleDocumentStatus.Rejected
                && (n.DocumentStatus == SaleDocumentStatus.Authorized
                    || (n.SriAuthorizationStatus != null
                        && n.SriAuthorizationStatus.Trim().ToUpper() == "AUTORIZADO"))
                && n.AuthorizationNumber != null && n.AuthorizationNumber.Trim() != ""
                && n.AccessKey != null && n.AccessKey.Trim() != "")
            .Select(n => new NoteProjection
            {
                SaleId = n.OriginalSaleId,
                Date = n.BusinessDate,
                Total = n.Total,
                Subtotal = n.Subtotal,
                ReturnedCost = n.InventoryReturnedAt != null && n.InventoryReturnedByUserId != null
                    ? n.Items.Sum(i => (decimal?)i.LineCost) ?? 0m
                    : 0m
            });
    }

    public static async Task<Dictionary<int, Totals>> LoadBySaleAsync(
        PosDbContext context, OperationalContext scope, int[] saleIds)
    {
        if (saleIds.Length == 0)
        {
            return new();
        }

        // No note date filter: each selected sale shows its lifetime net result.
        return await AuthorizedInContext(context, scope)
            .Where(n => saleIds.Contains(n.SaleId))
            .GroupBy(n => n.SaleId)
            .Select(g => new
            {
                SaleId = g.Key,
                Count = g.Count(),
                Total = g.Sum(n => n.Total),
                Subtotal = g.Sum(n => n.Subtotal),
                ReturnedCost = g.Sum(n => n.ReturnedCost)
            })
            .ToDictionaryAsync(n => n.SaleId, n => new Totals(n.Count, n.Total, n.Subtotal, n.ReturnedCost));
    }

    public static async Task<Dictionary<DateOnly, Totals>> LoadByDateAsync(
        PosDbContext context, OperationalContext scope, DateOnly firstDay, DateOnly lastDay)
    {
        return await AuthorizedInContext(context, scope)
            .Where(n => n.Date >= firstDay && n.Date <= lastDay)
            .GroupBy(n => n.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Total = g.Sum(n => n.Total),
                Subtotal = g.Sum(n => n.Subtotal),
                ReturnedCost = g.Sum(n => n.ReturnedCost)
            })
            .ToDictionaryAsync(n => n.Date, n => new Totals(n.Count, n.Total, n.Subtotal, n.ReturnedCost));
    }

    public static SaleCreditNoteImpactDto Calculate(
        decimal total, decimal subtotal, decimal totalCost, Totals? notes)
    {
        notes ??= new Totals(0, 0m, 0m, 0m);
        // Round the aggregated amounts once, then derive net values from those same amounts.
        var creditedTotal = RoundMoney(notes.Total);
        var creditedSubtotal = RoundMoney(notes.Subtotal);
        var returnedCost = RoundMoney(notes.ReturnedCost);
        var netSubtotal = RoundMoney(subtotal - creditedSubtotal);
        var netCost = RoundMoney(totalCost - returnedCost);
        var netProfit = RoundMoney(netSubtotal - netCost);

        return new SaleCreditNoteImpactDto
        {
            AuthorizedCreditNoteCount = notes.Count,
            AuthorizedCreditNoteTotal = creditedTotal,
            AuthorizedCreditNoteSubtotal = creditedSubtotal,
            ReturnedCost = returnedCost,
            NetTotal = RoundMoney(total - creditedTotal),
            NetSubtotal = netSubtotal,
            NetCost = netCost,
            NetGrossProfit = netProfit,
            NetGrossMarginPercent = netSubtotal > 0m
                ? Math.Round(netProfit / netSubtotal * 100m, 4, MidpointRounding.AwayFromZero)
                : 0m
        };
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal sealed record Totals(int Count, decimal Total, decimal Subtotal, decimal ReturnedCost);

    private sealed class NoteProjection
    {
        public int SaleId { get; init; }
        public DateOnly Date { get; init; }
        public decimal Total { get; init; }
        public decimal Subtotal { get; init; }
        public decimal ReturnedCost { get; init; }
    }
}
