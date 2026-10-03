import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { InventoryService } from './inventory.service';

describe('InventoryService bounded stock reads', () => {
  let service: InventoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(InventoryService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('sends defaults and paging plus existing stock filters', () => {
    service.getStocks().subscribe();
    const defaults = http.expectOne(request => request.url.endsWith('/api/inventory/stocks'));
    expect(defaults.request.params.get('page')).toBe('1');
    expect(defaults.request.params.get('pageSize')).toBe('30');
    defaults.flush({ items: [], summary: {} });
    service.getStocks('Mouse', 25, true, 3, 10).subscribe();
    const filtered = http.expectOne(request => request.url.endsWith('/api/inventory/stocks'));
    expect(filtered.request.params.get('search')).toBe('Mouse');
    expect(filtered.request.params.get('productId')).toBe('25');
    expect(filtered.request.params.get('onlyPositive')).toBe('true');
    expect(filtered.request.params.get('page')).toBe('3');
    expect(filtered.request.params.get('pageSize')).toBe('10');
    filtered.flush({ items: [], summary: {} });
  });

  it('uses the inventory-specific transfer lookup without stock page or tenant parameters', () => {
    service.getTransferProducts(' BARCODE ', 30).subscribe();
    const lookup = http.expectOne(request => request.url.endsWith('/api/inventory/transfer-products'));
    expect(lookup.request.method).toBe('GET');
    expect(lookup.request.params.get('search')).toBe('BARCODE');
    expect(lookup.request.params.get('take')).toBe('30');
    expect(lookup.request.params.has('page')).toBe(false);
    expect(lookup.request.params.has('companyId')).toBe(false);
    expect(lookup.request.params.has('establishmentId')).toBe(false);
    lookup.flush([]);
  });
});
