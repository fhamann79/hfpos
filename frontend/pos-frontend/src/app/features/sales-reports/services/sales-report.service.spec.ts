import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalesReportService } from './sales-report.service';

describe('SalesReportService credit note impact', () => {
  let service: SalesReportService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SalesReportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('falls back to original totals when impact is missing, including an old list without subtotal', () => {
    service.getSales({ from: '2026-07-01', to: '2026-07-01', search: ' cliente ' }).subscribe((rows) => {
      expect(rows[0].creditNoteImpact).toEqual({
        authorizedCreditNoteCount: 0, authorizedCreditNoteTotal: 0, authorizedCreditNoteSubtotal: 0,
        returnedCost: 0, netTotal: 115, netSubtotal: 100, netCost: 60, netGrossProfit: 40, netGrossMarginPercent: 40,
      });
    });
    const request = http.expectOne((req) => req.url.endsWith('/api/Sales'));
    expect(request.request.params.get('from')).toBe('2026-07-01');
    expect(request.request.params.get('to')).toBe('2026-07-01');
    expect(request.request.params.get('search')).toBe('cliente');
    request.flush([{ id: 1, total: 115, totalCost: 60, grossProfit: 40, grossMarginPercent: 40 }]);
  });

  it('maps the same persisted impact for list and detail without clamping negative values', () => {
    const impact = {
      authorizedCreditNoteCount: 2, authorizedCreditNoteTotal: 120, authorizedCreditNoteSubtotal: 110,
      returnedCost: 12, netTotal: -5, netSubtotal: -10, netCost: 48, netGrossProfit: -58, netGrossMarginPercent: 0,
    };
    const source = { id: 7, total: 115, subtotal: 100, totalCost: 60, creditNoteImpact: impact };
    service.getSales({}).subscribe((rows) => expect(rows[0].creditNoteImpact).toEqual(impact));
    http.expectOne((req) => req.url.endsWith('/api/Sales')).flush([source]);
    service.getSaleDetail(7).subscribe((detail) => expect(detail.creditNoteImpact).toEqual(impact));
    http.expectOne((req) => req.url.endsWith('/api/Sales/7')).flush(source);
  });

  it('reads numeric strings and safely falls back from invalid or partial impact values', () => {
    service.getSales({}).subscribe((rows) => {
      expect(rows[0].creditNoteImpact.authorizedCreditNoteCount).toBe(2);
      expect(rows[0].creditNoteImpact.netTotal).toBe(-5);
      expect(rows[0].creditNoteImpact.netSubtotal).toBe(100);
      expect(rows[0].creditNoteImpact.netCost).toBe(60);
    });
    http.expectOne((req) => req.url.endsWith('/api/Sales')).flush([{
      total: 115, subtotal: 100, totalCost: 60,
      creditNoteImpact: { authorizedCreditNoteCount: '2', netTotal: '-5', netSubtotal: 'invalid', netCost: null },
    }]);
  });
});
