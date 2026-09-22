import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  SaleDocumentStatus,
  SaleDocumentType,
  SaleStatus,
  SalesReportRow,
  SalesReportSummary,
} from '../../models/sales-report.model';
import { SalesReportService } from '../../services/sales-report.service';
import { SalesReportPage } from './sales-report-page';

const summary: SalesReportSummary = {
  salesCount: 48,
  totalSold: 4600,
  authorizedCreditNoteTotal: 120,
  authorizedCreditNoteCount: 2,
  netTotal: 4480,
  netCost: 2800,
  netGrossProfit: 1680,
  netGrossMarginPercent: 37.5,
  invoiceCount: 30,
  ticketCount: 18,
  voidedCount: 3,
  authorizedCount: 25,
};

function sale(overrides: Partial<SalesReportRow> = {}): SalesReportRow {
  return {
    id: 1,
    businessDate: '2026-07-01',
    timeZoneIdSnapshot: 'America/Guayaquil',
    createdAt: '2026-07-01T15:00:00Z',
    status: SaleStatus.Completed,
    number: '001-001-000000001',
    customerName: 'Cliente',
    customerIdentification: null,
    customerEmail: null,
    documentType: SaleDocumentType.Invoice,
    documentStatus: SaleDocumentStatus.Authorized,
    sriAuthorizationStatus: 'AUTORIZADO',
    total: 115,
    totalCost: 60,
    grossProfit: 40,
    grossMarginPercent: 40,
    itemsCount: 1,
    userId: 1,
    username: 'Operador',
    notes: null,
    creditNoteImpact: {
      authorizedCreditNoteCount: 2,
      authorizedCreditNoteTotal: 23,
      authorizedCreditNoteSubtotal: 20,
      returnedCost: 12,
      netTotal: 92,
      netSubtotal: 80,
      netCost: 48,
      netGrossProfit: 32,
      netGrossMarginPercent: 40,
    },
    ...overrides,
  };
}

describe('SalesReportPage server-side pagination', () => {
  let component: SalesReportPage;
  let service: {
    getSales: ReturnType<typeof vi.fn>;
    exportSales: ReturnType<typeof vi.fn>;
    getSaleDetail: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      getSales: vi.fn(() =>
        of({ items: [sale()], page: 1, pageSize: 15, totalItems: 48, totalPages: 4, summary })
      ),
      exportSales: vi.fn(() => of(new Blob(['csv'], { type: 'text/csv' }))),
      getSaleDetail: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: SalesReportService, useValue: service },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    });
    component = TestBed.runInInjectionContext(() => new SalesReportPage());
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('loads a bounded first page and uses the global backend summary for KPIs', () => {
    component.ngOnInit();

    expect(service.getSales).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, pageSize: 15, includeSummary: true })
    );
    expect(component.sales().map((row) => row.id)).toEqual([1]);
    expect(component.totalItems()).toBe(48);
    expect(component.totalPages()).toBe(4);
    expect(component.salesCount()).toBe(48);
    expect(component.netTotal()).toBe(4480);
    expect(component.netGrossMarginPercent()).toBe(37.5);
  });

  it('translates a PrimeNG lazy event into the correct page, rows and allowlisted server sort', () => {
    component.onSalesLazyLoad({ first: 30, rows: 30, sortField: 'total', sortOrder: 1 });

    expect(service.getSales).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, pageSize: 30, sortBy: 'total', sortDirection: 'asc' })
    );

    component.onSalesLazyLoad({ first: 30, rows: 30, sortField: 'total' });
    expect(service.getSales).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2, pageSize: 30, sortBy: 'total', sortDirection: 'asc' })
    );
  });

  it('does not accept a derived page-local sort field', () => {
    component.sortField = 'createdAt';
    component.sortOrder = -1;

    component.onSalesLazyLoad({
      first: 15,
      rows: 15,
      sortField: 'creditNoteImpact.netTotal',
      sortOrder: 1,
    });

    expect(service.getSales).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, sortBy: 'createdAt', sortDirection: 'asc' })
    );
  });

  it('preserves filters and resets pagination when applying or clearing them', () => {
    component.currentPage.set(4);
    component.first = 45;
    component.search = 'cliente';
    component.status = SaleStatus.Completed;
    component.applyFilters();

    expect(service.getSales).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, pageSize: 15, search: 'cliente', status: SaleStatus.Completed })
    );
    expect(component.first).toBe(0);

    component.clearFilters();
    expect(service.getSales).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, search: null, status: null })
    );
  });

  it('downloads CSV from the export endpoint instead of serializing the visible page', () => {
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:report'),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    component.summary.set(summary);
    component.search = 'todo el filtro';
    component.sales.set([sale()]);

    component.exportCsv();

    expect(service.exportSales).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, search: 'todo el filtro', includeSummary: false })
    );
    expect(URL.createObjectURL).toHaveBeenCalledOnce();
  });
});
