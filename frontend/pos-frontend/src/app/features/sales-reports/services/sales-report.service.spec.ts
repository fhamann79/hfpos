import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesReportQuery } from '../models/sales-report.model';
import { SalesReportService } from './sales-report.service';

const query: SalesReportQuery = {
  from: '2026-07-01',
  to: '2026-07-31',
  search: ' cliente ',
  status: null,
  documentType: null,
  documentStatus: null,
  userId: 7,
  page: 2,
  pageSize: 15,
  includeSummary: true,
  sortBy: 'total',
  sortDirection: 'asc',
};

describe('SalesReportService', () => {
  let service: SalesReportService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SalesReportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters, pagination and server sorting and maps the paged summary', () => {
    service.getSales(query).subscribe((result) => {
      expect(result.page).toBe(2);
      expect(result.pageSize).toBe(15);
      expect(result.totalItems).toBe(31);
      expect(result.totalPages).toBe(3);
      expect(result.items[0].creditNoteImpact.netTotal).toBe(92);
      expect(result.summary?.salesCount).toBe(31);
      expect(result.summary?.netTotal).toBe(3092);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Sales'));
    expect(request.request.params.get('from')).toBe('2026-07-01');
    expect(request.request.params.get('to')).toBe('2026-07-31');
    expect(request.request.params.get('search')).toBe('cliente');
    expect(request.request.params.get('userId')).toBe('7');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('15');
    expect(request.request.params.get('includeSummary')).toBe('true');
    expect(request.request.params.get('sortBy')).toBe('total');
    expect(request.request.params.get('sortDirection')).toBe('asc');
    request.flush({
      items: [
        {
          id: 1,
          total: 115,
          subtotal: 100,
          totalCost: 60,
          grossProfit: 40,
          grossMarginPercent: 40,
          creditNoteImpact: { netTotal: 92 },
        },
      ],
      page: 2,
      pageSize: 15,
      totalItems: 31,
      totalPages: 3,
      summary: { salesCount: 31, netTotal: 3092 },
    });
  });

  it('falls back to original totals when a page item has no credit-note impact', () => {
    service.getSales(query).subscribe((result) => {
      expect(result.items[0].creditNoteImpact).toEqual({
        authorizedCreditNoteCount: 0,
        authorizedCreditNoteTotal: 0,
        authorizedCreditNoteSubtotal: 0,
        returnedCost: 0,
        netTotal: 115,
        netSubtotal: 100,
        netCost: 60,
        netGrossProfit: 40,
        netGrossMarginPercent: 40,
      });
    });

    http.expectOne((candidate) => candidate.url.endsWith('/api/Sales')).flush({
      items: [{ id: 1, total: 115, totalCost: 60, grossProfit: 40, grossMarginPercent: 40 }],
      page: 2,
      pageSize: 15,
      totalItems: 1,
      totalPages: 1,
      summary: null,
    });
  });

  it('maps the same persisted impact for list and detail without clamping negative values', () => {
    const impact = {
      authorizedCreditNoteCount: 2,
      authorizedCreditNoteTotal: 120,
      authorizedCreditNoteSubtotal: 110,
      returnedCost: 12,
      netTotal: -5,
      netSubtotal: -10,
      netCost: 48,
      netGrossProfit: -58,
      netGrossMarginPercent: 0,
    };
    const source = { id: 7, total: 115, subtotal: 100, totalCost: 60, creditNoteImpact: impact };

    service.getSales(query).subscribe((result) => expect(result.items[0].creditNoteImpact).toEqual(impact));
    http.expectOne((candidate) => candidate.url.endsWith('/api/Sales')).flush({
      items: [source],
      page: 1,
      pageSize: 15,
      totalItems: 1,
      totalPages: 1,
    });

    service.getSaleDetail(7).subscribe((detail) => expect(detail.creditNoteImpact).toEqual(impact));
    http.expectOne((candidate) => candidate.url.endsWith('/api/Sales/7')).flush(source);
  });

  it('exports the complete filtered dataset through the dedicated endpoint without pagination params', () => {
    const expected = new Blob(['csv'], { type: 'text/csv' });

    service.exportSales(query).subscribe((blob) => expect(blob).toBe(expected));

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Sales/export'));
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.get('from')).toBe('2026-07-01');
    expect(request.request.params.get('search')).toBe('cliente');
    expect(request.request.params.get('sortBy')).toBe('total');
    expect(request.request.params.has('page')).toBe(false);
    expect(request.request.params.has('pageSize')).toBe(false);
    expect(request.request.params.has('includeSummary')).toBe(false);
    request.flush(expected);
  });
});
