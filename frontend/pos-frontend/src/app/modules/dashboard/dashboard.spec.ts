import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { Dashboard } from './dashboard';
import { DashboardService } from './dashboard.service';
import { DashboardSummary } from './dashboard.model';

const summary: DashboardSummary = {
  generatedAt: '2026-06-17T00:00:00Z',
  salesToday: {
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
        { provide: Router, useValue: { navigateByUrl: jasmine.createSpy('navigateByUrl') } },
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
});
