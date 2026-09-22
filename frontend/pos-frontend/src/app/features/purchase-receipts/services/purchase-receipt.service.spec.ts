import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PurchaseReceiptStatus } from '../models/purchase-receipt.model';
import { PurchaseReceiptService } from './purchase-receipt.service';

describe('PurchaseReceiptService pagination', () => {
  let service: PurchaseReceiptService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PurchaseReceiptService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends server filters and page bounds and returns the global summary', () => {
    service
      .getAll({
        search: ' proveedor ',
        from: '2026-09-01',
        to: '2026-09-30',
        status: PurchaseReceiptStatus.Posted,
        page: 3,
        pageSize: 30,
      })
      .subscribe((result) => {
        expect(result.totalItems).toBe(75);
        expect(result.summary?.totalReceived).toBe(980.5);
      });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/PurchaseReceipts'));
    expect(request.request.params.get('search')).toBe('proveedor');
    expect(request.request.params.get('from')).toBe('2026-09-01');
    expect(request.request.params.get('to')).toBe('2026-09-30');
    expect(request.request.params.get('status')).toBe(String(PurchaseReceiptStatus.Posted));
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('30');
    request.flush({
      items: [],
      page: 3,
      pageSize: 30,
      totalItems: 75,
      totalPages: 3,
      summary: { postedCount: 70, canceledCount: 5, totalReceived: 980.5 },
    });
  });
});
