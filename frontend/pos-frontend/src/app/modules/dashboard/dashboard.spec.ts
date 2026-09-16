import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../core/services/permission.service';
import { Dashboard } from './dashboard';
import { DashboardService } from './dashboard.service';
import { DashboardNetSales, DashboardSalesToday, DashboardSummary } from './dashboard.model';

const emptyNetSales: DashboardNetSales = {
  creditNoteCount: 0,
  creditNoteTotal: 0,
  creditNoteSubtotal: 0,
  returnedCost: 0,
  netSales: 0,
  netSubtotal: 0,
  netCost: 0,
  netGrossProfit: 0,
  netGrossMarginPercent: 0,
};

const summary: DashboardSummary = {
  generatedAt: '2026-06-17T00:00:00Z',
  salesToday: {
    ...emptyNetSales,
    count: 0,
    totalSold: 0,
    totalCost: 0,
    grossProfit: 0,
    grossMarginPercent: 0,
    voidedCount: 0,
    invoiceCount: 0,
    ticketCount: 0,
    authorizedSriInvoiceCount: 0,
  },
  salesLastSevenDays: {
    ...emptyNetSales,
    count: 0,
    totalSold: 0,
    totalCost: 0,
    grossProfit: 0,
    grossMarginPercent: 0,
    days: [],
  },
  purchasesToday: {
    postedCount: 0,
    totalPurchased: 0,
    canceledCount: 0,
    canceledAmount: 0,
    netPurchased: 0,
  },
  purchasesLastSevenDays: {
    postedCount: 0,
    totalPurchased: 0,
    canceledCount: 0,
    canceledAmount: 0,
    netPurchased: 0,
    days: [],
  },
  inventory: {
    activeProducts: 0,
    zeroStockProducts: 0,
    lowStockProducts: 0,
    totalInventoryValue: 0,
    lowestStockProducts: [],
  },
  fiscal: {
    sriEnabled: false,
    certificateConfigured: false,
    certificateExpiresAt: null,
    certificateExpired: false,
    certificateExpiringSoon: false,
    emailEnabled: false,
    emailTested: false,
    emailLastTestSucceeded: false,
    emailLastTestedAt: null,
  },
  alerts: [],
};

describe('Dashboard', () => {
  let component: Dashboard;
  let fixture: ComponentFixture<Dashboard>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [
        { provide: DashboardService, useValue: { getSummary: () => of(summary) } },
        { provide: PermissionService, useValue: { canAccess: () => true } },
        { provide: Router, useValue: { navigateByUrl: vi.fn() } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Dashboard);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  async function showSales(overrides: Partial<DashboardSalesToday> = {}): Promise<void> {
    const salesToday: DashboardSalesToday = {
      ...summary.salesToday,
      count: 1,
      totalSold: 100,
      totalCost: 60,
      grossProfit: 40,
      grossMarginPercent: 40,
      netSales: 100,
      netSubtotal: 100,
      netCost: 60,
      netGrossProfit: 40,
      netGrossMarginPercent: 40,
      ...overrides,
    };
    component.summary.set({
      ...summary,
      salesToday,
      salesLastSevenDays: {
        ...salesToday,
        days: [{ ...salesToday, date: '2026-06-17' }],
      },
    });
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function text(selector: string): string {
    return (fixture.nativeElement as HTMLElement).querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  it('shows gross and net as equal when there are no credit notes', async () => {
    await showSales();
    expect(component.summary()?.salesToday.netSales).toBe(component.summary()?.salesToday.totalSold);
    expect(text('.metric-card--money small')).toBe('Venta neta hoy');
    expect(text('.metric-card--money strong')).toBe('$100.00');
    expect(text('.metric-card--money')).toContain('Bruto $100.00 / NC $0.00');
    expect(text('.metric-card--money')).toContain('0 NC autorizadas');
    expect(text('.metric-card--profit')).toContain('Costo neto $60.00');
  });

  it('shows reduced income with unchanged cost for a note without physical return', async () => {
    await showSales({
      creditNoteCount: 1, creditNoteTotal: 20, creditNoteSubtotal: 20,
      netSales: 80, netSubtotal: 80, netGrossProfit: 20, netGrossMarginPercent: 25,
    });
    expect(text('.metric-card--money strong')).toBe('$80.00');
    expect(text('.metric-card--money')).toContain('1 NC autorizadas');
    expect(text('.metric-card--profit small')).toBe('Utilidad neta hoy');
    expect(text('.metric-card--profit strong')).toBe('$20.00');
    expect(text('.metric-card--profit')).toContain('Costo neto $60.00');
    expect(text('.metric-card--margin')).toContain(component.marginLabel(25));
    expect(text('.sales-period-totals')).toContain('Notas de crédito$20.00');
  });

  it('shows reduced income and reversed historical cost after a physical return', async () => {
    await showSales({
      creditNoteCount: 1, creditNoteTotal: 20, creditNoteSubtotal: 20, returnedCost: 12,
      netSales: 80, netSubtotal: 80, netCost: 48, netGrossProfit: 32, netGrossMarginPercent: 40,
    });
    expect(text('.metric-card--money strong')).toBe('$80.00');
    expect(text('.metric-card--profit')).toContain('Costo neto $48.00');
    expect(text('.metric-card--profit strong')).toBe('$32.00');
    expect(text('.sales-period-totals')).toContain('Utilidad neta$32.00');
  });

  it('renders negative net sales and profit without hiding the trend bars', async () => {
    await showSales({
      count: 0, totalSold: 0, totalCost: 0, grossProfit: 0, grossMarginPercent: 0,
      creditNoteCount: 1, creditNoteTotal: 25, creditNoteSubtotal: 25, returnedCost: 15,
      netSales: -25, netSubtotal: -25, netCost: -15, netGrossProfit: -10, netGrossMarginPercent: 0,
    });
    expect(text('.metric-card--money')).toContain('-$25.00');
    expect(text('.metric-card--profit')).toContain('-$10.00');
    expect(text('.trend-row--sales')).toContain('-$25.00');
    expect(text('.trend-row--sales')).toContain('-$10.00');
    expect(text('.trend-row--sales time')).toBe('17/06');
    expect(component.dayBarWidth(-25)).toBe('100%');
    expect(component.profitBarWidth(-10)).toBe('100%');
    const bars = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.trend-bar-negative');
    expect(bars.length).toBe(2);
    expect([...bars].every((bar) => bar.style.width === '100%')).toBe(true);
  });

  it('uses net values as the scale and keeps zero activity bars empty', async () => {
    await showSales({ netSales: 80, netGrossProfit: 20 });
    expect(component.dayBarWidth(40)).toBe('50%');
    expect(component.profitBarWidth(-10)).toBe('50%');
    expect(component.dayBarWidth(0)).toBe('0%');
    expect(component.profitBarWidth(0)).toBe('0%');
  });
});
