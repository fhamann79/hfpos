export type DashboardAlertSeverity = 'success' | 'info' | 'warn' | 'danger' | 'secondary';

export interface DashboardSummary {
  generatedAt: string;
  salesToday: DashboardSalesToday;
  salesLastSevenDays: DashboardSalesLastSevenDays;
  inventory: DashboardInventorySummary;
  fiscal: DashboardFiscalSummary;
  alerts: DashboardAlert[];
}

export interface DashboardSalesToday {
  count: number;
  totalSold: number;
  voidedCount: number;
  invoiceCount: number;
  ticketCount: number;
  authorizedSriInvoiceCount: number;
}

export interface DashboardSalesLastSevenDays {
  count: number;
  totalSold: number;
  days: DashboardDailySales[];
}

export interface DashboardDailySales {
  date: string;
  count: number;
  totalSold: number;
}

export interface DashboardInventorySummary {
  activeProducts: number;
  zeroStockProducts: number;
  lowStockProducts: number;
  lowStockThreshold: number;
  lowestStockProducts: DashboardLowStockProduct[];
}

export interface DashboardLowStockProduct {
  productId: number;
  productName: string;
  categoryName: string;
  quantity: number;
}

export interface DashboardFiscalSummary {
  sriEnabled: boolean;
  certificateConfigured: boolean;
  certificateExpiresAt: string | null;
  certificateExpired: boolean;
  certificateExpiringSoon: boolean;
  emailEnabled: boolean;
  emailTested: boolean;
  emailLastTestSucceeded: boolean;
  emailLastTestedAt: string | null;
}

export interface DashboardAlert {
  category: string;
  severity: DashboardAlertSeverity;
  title: string;
  message: string;
  count: number | null;
}
