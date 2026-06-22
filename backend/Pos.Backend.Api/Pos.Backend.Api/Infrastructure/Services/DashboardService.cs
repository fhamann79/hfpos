using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private const int CertificateExpiringSoonDays = 30;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly IBusinessClockService _businessClock;

    public DashboardService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        IBusinessClockService businessClock)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _businessClock = businessClock;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var timeZoneId = operationalContext.CompanyTimeZoneId;
        var now = _businessClock.UtcNow;
        var today = _businessClock.GetBusinessDate(now, timeZoneId);
        var firstDay = today.AddDays(-6);
        var rangeStartUtc = _businessClock.GetBusinessDateStartUtc(firstDay, timeZoneId);
        var rangeEndUtc = _businessClock.GetBusinessDateStartUtc(today.AddDays(1), timeZoneId);

        var sales = await LoadSalesAsync(
            operationalContext.CompanyId,
            operationalContext.EstablishmentId,
            operationalContext.EmissionPointId,
            rangeStartUtc,
            rangeEndUtc,
            timeZoneId);

        var purchaseReceipts = await LoadPurchaseReceiptsAsync(
            operationalContext.CompanyId,
            operationalContext.EstablishmentId,
            rangeStartUtc,
            rangeEndUtc,
            timeZoneId);

        var inventory = await BuildInventorySummaryAsync(
            operationalContext.CompanyId,
            operationalContext.EstablishmentId);

        var fiscal = await BuildFiscalSummaryAsync(operationalContext.CompanyId, now);
        var salesToday = BuildSalesToday(sales, today);
        var salesLastSevenDays = BuildSalesLastSevenDays(sales, firstDay);
        var purchasesToday = BuildPurchasesToday(purchaseReceipts, today);
        var purchasesLastSevenDays = BuildPurchasesLastSevenDays(purchaseReceipts, firstDay);
        var alerts = BuildAlerts(inventory, fiscal);

        return new DashboardSummaryDto
        {
            GeneratedAt = now,
            SalesToday = salesToday,
            SalesLastSevenDays = salesLastSevenDays,
            PurchasesToday = purchasesToday,
            PurchasesLastSevenDays = purchasesLastSevenDays,
            Inventory = inventory,
            Fiscal = fiscal,
            Alerts = alerts
        };
    }

    private async Task<IReadOnlyList<SaleSnapshot>> LoadSalesAsync(
        int companyId,
        int establishmentId,
        int emissionPointId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        string timeZoneId)
    {
        var rawSales = await _context.Sales
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId
                && s.EstablishmentId == establishmentId
                && s.EmissionPointId == emissionPointId
                && s.CreatedAt >= rangeStartUtc
                && s.CreatedAt < rangeEndUtc)
            .Select(s => new
            {
                s.CreatedAt,
                s.Status,
                s.DocumentType,
                s.DocumentStatus,
                s.SriAuthorizationStatus,
                s.Total,
                s.Subtotal,
                s.TotalCost,
                s.GrossProfit
            })
            .ToListAsync();

        return rawSales
            .Select(s => new SaleSnapshot(
                _businessClock.GetBusinessDate(s.CreatedAt, timeZoneId),
                s.Status,
                s.DocumentType,
                s.DocumentStatus,
                s.SriAuthorizationStatus,
                s.Total,
                s.Subtotal,
                s.TotalCost,
                s.GrossProfit))
            .ToList();
    }

    private async Task<IReadOnlyList<PurchaseReceiptSnapshot>> LoadPurchaseReceiptsAsync(
        int companyId,
        int establishmentId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        string timeZoneId)
    {
        var rawReceipts = await _context.PurchaseReceipts
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                && r.EstablishmentId == establishmentId
                && ((r.Status == PurchaseReceiptStatus.Posted
                        && r.ReceiptDate >= rangeStartUtc
                        && r.ReceiptDate < rangeEndUtc)
                    || (r.Status == PurchaseReceiptStatus.Canceled
                        && r.CanceledAt.HasValue
                        && r.CanceledAt.Value >= rangeStartUtc
                        && r.CanceledAt.Value < rangeEndUtc)))
            .Select(r => new
            {
                r.Status,
                r.ReceiptDate,
                r.CanceledAt,
                r.Subtotal
            })
            .ToListAsync();

        return rawReceipts
            .Select(r => new PurchaseReceiptSnapshot(
                _businessClock.GetBusinessDate(
                    r.Status == PurchaseReceiptStatus.Canceled && r.CanceledAt.HasValue
                        ? r.CanceledAt.Value
                        : r.ReceiptDate,
                    timeZoneId),
                r.Status,
                r.Subtotal))
            .ToList();
    }

    private async Task<DashboardInventorySummaryDto> BuildInventorySummaryAsync(int companyId, int establishmentId)
    {
        var stocks = await _context.Products
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Select(p => new
            {
                ProductId = p.Id,
                ProductName = p.Name,
                CategoryName = p.Category.Name,
                MinimumStock = p.MinimumStock,
                Cost = p.Cost,
                Quantity = _context.ProductStocks
                    .Where(s => s.ProductId == p.Id
                        && s.CompanyId == companyId
                        && s.EstablishmentId == establishmentId)
                    .Select(s => (decimal?)s.Quantity)
                    .FirstOrDefault() ?? 0m
            })
            .ToListAsync();

        return new DashboardInventorySummaryDto
        {
            ActiveProducts = stocks.Count,
            ZeroStockProducts = stocks.Count(s => s.Quantity <= 0m),
            LowStockProducts = stocks.Count(s => IsLowStock(s.Quantity, s.MinimumStock)),
            TotalInventoryValue = stocks.Sum(s => s.Quantity * s.Cost),
            LowestStockProducts = stocks
                .OrderBy(s => s.Quantity)
                .ThenBy(s => s.ProductName)
                .Take(5)
                .Select(s => new DashboardLowStockProductDto
                {
                    ProductId = s.ProductId,
                    ProductName = s.ProductName,
                    CategoryName = s.CategoryName,
                    Quantity = s.Quantity,
                    MinimumStock = s.MinimumStock
                })
                .ToList()
        };
    }

    private async Task<DashboardFiscalSummaryDto> BuildFiscalSummaryAsync(int companyId, DateTime now)
    {
        var sriSettings = await _context.CompanySriSettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new
            {
                s.IsEnabled,
                s.CertificateConfigured,
                s.CertificateExpiresAt
            })
            .FirstOrDefaultAsync();

        var activeCertificate = await _context.CompanySriCertificates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderByDescending(c => c.UploadedAt)
            .Select(c => new
            {
                c.NotAfter
            })
            .FirstOrDefaultAsync();

        var emailSettings = await _context.CompanyEmailSettings
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => new
            {
                s.IsEnabled,
                s.LastTestedAt,
                s.LastTestSucceeded
            })
            .FirstOrDefaultAsync();

        var certificateExpiresAt = sriSettings?.CertificateExpiresAt ?? activeCertificate?.NotAfter;
        var certificateConfigured = sriSettings?.CertificateConfigured == true || activeCertificate is not null;
        var certificateExpired = certificateExpiresAt.HasValue && certificateExpiresAt.Value <= now;
        var certificateExpiringSoon = certificateExpiresAt.HasValue
            && !certificateExpired
            && certificateExpiresAt.Value <= now.AddDays(CertificateExpiringSoonDays);

        return new DashboardFiscalSummaryDto
        {
            SriEnabled = sriSettings?.IsEnabled == true,
            CertificateConfigured = certificateConfigured,
            CertificateExpiresAt = certificateExpiresAt,
            CertificateExpired = certificateExpired,
            CertificateExpiringSoon = certificateExpiringSoon,
            EmailEnabled = emailSettings?.IsEnabled == true,
            EmailTested = emailSettings?.LastTestedAt is not null,
            EmailLastTestSucceeded = emailSettings?.LastTestSucceeded == true,
            EmailLastTestedAt = emailSettings?.LastTestedAt
        };
    }

    private static DashboardSalesTodayDto BuildSalesToday(IReadOnlyList<SaleSnapshot> sales, DateOnly today)
    {
        var todaySales = sales.Where(s => s.Date == today).ToList();
        var validSales = todaySales.Where(s => s.Status != SaleStatus.Voided).ToList();
        var subtotal = validSales.Sum(s => s.Subtotal);
        var grossProfit = validSales.Sum(s => s.GrossProfit);

        return new DashboardSalesTodayDto
        {
            Count = validSales.Count,
            TotalSold = validSales.Sum(s => s.Total),
            TotalCost = validSales.Sum(s => s.TotalCost),
            GrossProfit = grossProfit,
            GrossMarginPercent = CalculateGrossMarginPercent(grossProfit, subtotal),
            VoidedCount = todaySales.Count(s => s.Status == SaleStatus.Voided),
            InvoiceCount = validSales.Count(s => s.DocumentType == SaleDocumentType.Invoice),
            TicketCount = validSales.Count(s => s.DocumentType == SaleDocumentType.Ticket),
            AuthorizedSriInvoiceCount = validSales.Count(IsAuthorizedInvoice)
        };
    }

    private static DashboardSalesLastSevenDaysDto BuildSalesLastSevenDays(IReadOnlyList<SaleSnapshot> sales, DateOnly firstDay)
    {
        var validSales = sales.Where(s => s.Status != SaleStatus.Voided).ToList();
        var days = Enumerable.Range(0, 7)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day =>
            {
                var daySales = validSales.Where(s => s.Date == day).ToList();

                return new DashboardDailySalesDto
                {
                    Date = day,
                    Count = daySales.Count,
                    TotalSold = daySales.Sum(s => s.Total),
                    GrossProfit = daySales.Sum(s => s.GrossProfit)
                };
            })
            .ToList();

        var subtotal = validSales.Sum(s => s.Subtotal);
        var grossProfit = validSales.Sum(s => s.GrossProfit);

        return new DashboardSalesLastSevenDaysDto
        {
            Count = validSales.Count,
            TotalSold = validSales.Sum(s => s.Total),
            TotalCost = validSales.Sum(s => s.TotalCost),
            GrossProfit = grossProfit,
            GrossMarginPercent = CalculateGrossMarginPercent(grossProfit, subtotal),
            Days = days
        };
    }

    private static DashboardPurchasesTodayDto BuildPurchasesToday(
        IReadOnlyList<PurchaseReceiptSnapshot> purchaseReceipts,
        DateOnly today)
    {
        var todayReceipts = purchaseReceipts.Where(r => r.Date == today).ToList();
        var postedReceipts = todayReceipts.Where(r => r.Status == PurchaseReceiptStatus.Posted).ToList();
        var canceledReceipts = todayReceipts.Where(r => r.Status == PurchaseReceiptStatus.Canceled).ToList();
        var totalPurchased = postedReceipts.Sum(r => r.Subtotal);
        var canceledAmount = canceledReceipts.Sum(r => r.Subtotal);

        return new DashboardPurchasesTodayDto
        {
            PostedCount = postedReceipts.Count,
            TotalPurchased = totalPurchased,
            CanceledCount = canceledReceipts.Count,
            CanceledAmount = canceledAmount,
            NetPurchased = totalPurchased - canceledAmount
        };
    }

    private static DashboardPurchasesLastSevenDaysDto BuildPurchasesLastSevenDays(
        IReadOnlyList<PurchaseReceiptSnapshot> purchaseReceipts,
        DateOnly firstDay)
    {
        var days = Enumerable.Range(0, 7)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => BuildDailyPurchases(purchaseReceipts.Where(r => r.Date == day).ToList(), day))
            .ToList();

        return new DashboardPurchasesLastSevenDaysDto
        {
            PostedCount = days.Sum(day => day.PostedCount),
            TotalPurchased = days.Sum(day => day.TotalPurchased),
            CanceledCount = days.Sum(day => day.CanceledCount),
            CanceledAmount = days.Sum(day => day.CanceledAmount),
            NetPurchased = days.Sum(day => day.NetPurchased),
            Days = days
        };
    }

    private static DashboardDailyPurchasesDto BuildDailyPurchases(
        IReadOnlyList<PurchaseReceiptSnapshot> purchaseReceipts,
        DateOnly day)
    {
        var postedReceipts = purchaseReceipts.Where(r => r.Status == PurchaseReceiptStatus.Posted).ToList();
        var canceledReceipts = purchaseReceipts.Where(r => r.Status == PurchaseReceiptStatus.Canceled).ToList();
        var totalPurchased = postedReceipts.Sum(r => r.Subtotal);
        var canceledAmount = canceledReceipts.Sum(r => r.Subtotal);

        return new DashboardDailyPurchasesDto
        {
            Date = day,
            PostedCount = postedReceipts.Count,
            TotalPurchased = totalPurchased,
            CanceledCount = canceledReceipts.Count,
            CanceledAmount = canceledAmount,
            NetPurchased = totalPurchased - canceledAmount
        };
    }

    private static List<DashboardAlertDto> BuildAlerts(
        DashboardInventorySummaryDto inventory,
        DashboardFiscalSummaryDto fiscal)
    {
        var alerts = new List<DashboardAlertDto>();

        if (!fiscal.SriEnabled)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "warn",
                Title = "SRI no habilitado",
                Message = "La emision electronica no esta habilitada para la empresa."
            });
        }

        if (!fiscal.CertificateConfigured)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "warn",
                Title = "Certificado no configurado",
                Message = "No hay un certificado digital activo para firmar comprobantes."
            });
        }
        else if (fiscal.CertificateExpired)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "danger",
                Title = "Certificado vencido",
                Message = "El certificado digital configurado ya vencio."
            });
        }
        else if (fiscal.CertificateExpiringSoon)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "warn",
                Title = "Certificado por vencer",
                Message = $"El certificado vence en los proximos {CertificateExpiringSoonDays} dias."
            });
        }

        if (!fiscal.EmailEnabled)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "info",
                Title = "Email empresarial deshabilitado",
                Message = "La configuracion de email no esta habilitada."
            });
        }
        else if (!fiscal.EmailLastTestSucceeded)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Fiscal",
                Severity = "warn",
                Title = "Email no probado",
                Message = "El email empresarial no tiene una prueba exitosa registrada."
            });
        }

        if (inventory.ZeroStockProducts > 0)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Inventario",
                Severity = "danger",
                Title = "Productos sin stock",
                Message = "Hay productos activos sin stock disponible.",
                Count = inventory.ZeroStockProducts
            });
        }

        if (inventory.LowStockProducts > 0)
        {
            alerts.Add(new DashboardAlertDto
            {
                Category = "Inventario",
                Severity = "warn",
                Title = "Stock bajo",
                Message = "Hay productos activos en o por debajo de su stock minimo configurado.",
                Count = inventory.LowStockProducts
            });
        }

        return alerts;
    }

    private static bool IsAuthorizedInvoice(SaleSnapshot sale)
    {
        return sale.DocumentType == SaleDocumentType.Invoice
            && (sale.DocumentStatus == SaleDocumentStatus.Authorized
                || string.Equals(sale.SriAuthorizationStatus, "AUTORIZADO", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLowStock(decimal quantity, decimal minimumStock)
    {
        return minimumStock > 0m && quantity > 0m && quantity <= minimumStock;
    }

    private static decimal CalculateGrossMarginPercent(decimal grossProfit, decimal subtotal)
    {
        return subtotal > 0m
            ? Math.Round(grossProfit / subtotal * 100m, 4, MidpointRounding.AwayFromZero)
            : 0m;
    }

    private sealed record SaleSnapshot(
        DateOnly Date,
        SaleStatus Status,
        SaleDocumentType DocumentType,
        SaleDocumentStatus DocumentStatus,
        string? SriAuthorizationStatus,
        decimal Total,
        decimal Subtotal,
        decimal TotalCost,
        decimal GrossProfit);

    private sealed record PurchaseReceiptSnapshot(
        DateOnly Date,
        PurchaseReceiptStatus Status,
        decimal Subtotal);
}
