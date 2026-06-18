namespace Pos.Backend.Api.Core.DTOs;

public class DashboardSummaryDto
{
    public DateTime GeneratedAt { get; set; }

    public DashboardSalesTodayDto SalesToday { get; set; } = new();

    public DashboardSalesLastSevenDaysDto SalesLastSevenDays { get; set; } = new();

    public DashboardInventorySummaryDto Inventory { get; set; } = new();

    public DashboardFiscalSummaryDto Fiscal { get; set; } = new();

    public List<DashboardAlertDto> Alerts { get; set; } = new();
}

public class DashboardSalesTodayDto
{
    public int Count { get; set; }

    public decimal TotalSold { get; set; }

    public int VoidedCount { get; set; }

    public int InvoiceCount { get; set; }

    public int TicketCount { get; set; }

    public int AuthorizedSriInvoiceCount { get; set; }
}

public class DashboardSalesLastSevenDaysDto
{
    public int Count { get; set; }

    public decimal TotalSold { get; set; }

    public List<DashboardDailySalesDto> Days { get; set; } = new();
}

public class DashboardDailySalesDto
{
    public DateOnly Date { get; set; }

    public int Count { get; set; }

    public decimal TotalSold { get; set; }
}

public class DashboardInventorySummaryDto
{
    public int ActiveProducts { get; set; }

    public int ZeroStockProducts { get; set; }

    public int LowStockProducts { get; set; }

    public List<DashboardLowStockProductDto> LowestStockProducts { get; set; } = new();
}

public class DashboardLowStockProductDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal MinimumStock { get; set; }
}

public class DashboardFiscalSummaryDto
{
    public bool SriEnabled { get; set; }

    public bool CertificateConfigured { get; set; }

    public DateTime? CertificateExpiresAt { get; set; }

    public bool CertificateExpired { get; set; }

    public bool CertificateExpiringSoon { get; set; }

    public bool EmailEnabled { get; set; }

    public bool EmailTested { get; set; }

    public bool EmailLastTestSucceeded { get; set; }

    public DateTime? EmailLastTestedAt { get; set; }
}

public class DashboardAlertDto
{
    public string Category { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? Count { get; set; }
}
