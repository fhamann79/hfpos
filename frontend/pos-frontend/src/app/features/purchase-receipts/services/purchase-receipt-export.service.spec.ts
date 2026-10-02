import { HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PurchaseReceiptStatus } from '../models/purchase-receipt.model';
import { PurchaseReceiptService } from './purchase-receipt.service';

describe('PurchaseReceiptService CSV export', () => {
  let service: PurchaseReceiptService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PurchaseReceiptService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('sends every current filter without pagination and observes blob response headers', () => {
    const body = new Blob(['csv']);
    service.exportCsv({ search: ' supplier ', from: '2026-09-01', to: '2026-09-30',
      status: PurchaseReceiptStatus.Canceled }).subscribe(response => {
      expect(response.body).toBe(body);
      expect(response.headers.get('Content-Disposition')).toBe('attachment; filename="receipts.csv"');
    });
    const req = http.expectOne(r => r.url.endsWith('/api/PurchaseReceipts/export'));
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.keys().sort()).toEqual(['from', 'search', 'status', 'to']);
    expect(req.request.params.get('search')).toBe('supplier');
    expect(req.request.params.get('from')).toBe('2026-09-01');
    expect(req.request.params.get('to')).toBe('2026-09-30');
    expect(req.request.params.get('status')).toBe('2');
    req.flush(body, { headers: new HttpHeaders({ 'Content-Disposition': 'attachment; filename="receipts.csv"' }) });
  });

  it('omits empty optional filters', () => {
    service.exportCsv({ search: ' ', from: '', to: '', status: null }).subscribe();
    const req = http.expectOne(r => r.url.endsWith('/api/PurchaseReceipts/export'));
    expect(req.request.params.keys()).toEqual([]);
    req.flush(new Blob(['headers']));
  });
});
