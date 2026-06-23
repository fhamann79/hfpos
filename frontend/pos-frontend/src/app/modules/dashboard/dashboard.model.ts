export type DashboardAlertSeverity = 'success' | 'info' | 'warn' | 'danger' | 'secondary';

export interface DashboardSummary {
  generatedAt: string;
  salesToday: DashboardSalesToday;
  salesLastSevenDays: DashboardSalesLastSevenDays;
  purchasesToday: DashboardPurchasesToday;
  purchasesLastSevenDays: DashboardPurchasesLastSevenDays;
  inventory: DashboardInventorySummary;
  fiscal: DashboardFiscalSummary;
  alerts: DashboardAlert[];
}

export interface DashboardSalesToday {
  count: number;
  totalSold: number;
  totalCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  voidedCount: number;
  invoiceCount: number;
  ticketCount: number;
  authorizedSriInvoiceCount: number;
}

export interface DashboardSalesLastSevenDays {
  count: number;
  totalSold: number;
  totalCost: number;
  grossProfit: number;
  grossMarginPercent: number;
  days: DashboardDailySales[];
}

export interface DashboardDailySales {
  date: string;
  count: number;
  totalSold: number;
  grossProfit: number;
}

export interface DashboardPurchasesToday {
  postedCount: number;
  totalPurchased: number;
  canceledCount: number;
  canceledAmount: number;
  netPurchased: number;
}

export interface DashboardPurchasesLastSevenDays {
  postedCount: number;
  totalPurchased: number;
  canceledCount: number;
  canceledAmount: number;
  netPurchased: number;
  days: DashboardDailyPurchases[];
}

export interface DashboardDailyPurchases {
  date: string;
  postedCount: number;
  totalPurchased: number;
  canceledCount: number;
  canceledAmount: number;
  netPurchased: number;
}

export interface DashboardInventorySummary {
  activeProducts: number;
  zeroStockProducts: number;
  lowStockProducts: number;
  totalInventoryValue: number;
  lowestStockProducts: DashboardLowStockProduct[];
}

export interface DashboardLowStockProduct {
  productId: number;
  productName: string;
  categoryName: string;
  quantity: number;
  minimumStock: number;
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
