using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private const decimal LowStockThreshold = 3m;
    private const int CertificateExpiringSoonDays = 30;

    private readonly PosDbContext _context;
    private readonly IOperationalContextAccessor _operationalContextAccessor;
    private readonly ISriFiscalClock _fiscalClock;

    public DashboardService(
        PosDbContext context,
        IOperationalContextAccessor operationalContextAccessor,
        ISriFiscalClock fiscalClock)
    {
        _context = context;
        _operationalContextAccessor = operationalContextAccessor;
        _fiscalClock = fiscalClock;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var operationalContext = await _operationalContextAccessor.GetRequiredContextAsync();
        var now = _fiscalClock.UtcNow;
        var today = _fiscalClock.GetEcuadorFiscalDate(now);
        var firstDay = today.AddDays(-6);
        var rangeStartUtc = EcuadorDateStartUtc(firstDay);
        var rangeEndUtc = EcuadorDateStartUtc(today.AddDays(1));

        var sales = await LoadSalesAsync(
            operationalContext.CompanyId,
            operationalContext.EstablishmentId,
            operationalContext.EmissionPointId,
            rangeStartUtc,
            rangeEndUtc);

        var inventory = await BuildInventorySummaryAsync(
            operationalContext.CompanyId,
            operationalContext.EstablishmentId);

        var fiscal = await BuildFiscalSummaryAsync(operationalContext.CompanyId, now);
        var salesToday = BuildSalesToday(sales, today);
        var salesLastSevenDays = BuildSalesLastSevenDays(sales, firstDay);
        var alerts = BuildAlerts(inventory, fiscal);

        return new DashboardSummaryDto
        {
            GeneratedAt = now,
            SalesToday = salesToday,
            SalesLastSevenDays = salesLastSevenDays,
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
        DateTime rangeEndUtc)
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
                s.Total
            })
            .ToListAsync();

        return rawSales
            .Select(s => new SaleSnapshot(
                _fiscalClock.GetEcuadorFiscalDate(s.CreatedAt),
                s.Status,
                s.DocumentType,
                s.DocumentStatus,
                s.SriAuthorizationStatus,
                s.Total))
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
            LowStockProducts = stocks.Count(s => s.Quantity > 0m && s.Quantity <= LowStockThreshold),
            LowStockThreshold = LowStockThreshold,
            LowestStockProducts = stocks
                .OrderBy(s => s.Quantity)
                .ThenBy(s => s.ProductName)
                .Take(5)
                .Select(s => new DashboardLowStockProductDto
                {
                    ProductId = s.ProductId,
                    ProductName = s.ProductName,
                    CategoryName = s.CategoryName,
                    Quantity = s.Quantity
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

        return new DashboardSalesTodayDto
        {
            Count = validSales.Count,
            TotalSold = validSales.Sum(s => s.Total),
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
                    TotalSold = daySales.Sum(s => s.Total)
                };
            })
            .ToList();

        return new DashboardSalesLastSevenDaysDto
        {
            Count = validSales.Count,
            TotalSold = validSales.Sum(s => s.Total),
            Days = days
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
                Message = $"Hay productos activos con stock entre 1 y {LowStockThreshold}.",
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

    private static DateTime EcuadorDateStartUtc(DateOnly date)
    {
        return DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(5), DateTimeKind.Utc);
    }

    private sealed record SaleSnapshot(
        DateOnly Date,
        SaleStatus Status,
        SaleDocumentType DocumentType,
        SaleDocumentStatus DocumentStatus,
        string? SriAuthorizationStatus,
        decimal Total);
}
