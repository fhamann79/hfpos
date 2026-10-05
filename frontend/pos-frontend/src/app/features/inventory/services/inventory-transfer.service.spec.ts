import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { InventoryService } from './inventory.service';
import { InventoryTransferCreateRequest } from '../models/inventory-transfer.model';

describe('InventoryService transfer API', () => {
  let service: InventoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(InventoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts an idempotent transfer and reads its detail', () => {
    const payload: InventoryTransferCreateRequest = {
      destinationEstablishmentId: 2, requestId: crypto.randomUUID(),
      reference: null, notes: null, items: [{ productId: 3, quantity: 2 }],
    };
    service.createTransfer(payload).subscribe();
    const post = http.expectOne(request => request.url.endsWith('/api/inventory/transfers'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(payload);
    post.flush({ id: 41, requestId: payload.requestId });

    service.getTransferById(41).subscribe();
    const detail = http.expectOne(request => request.url.endsWith('/api/inventory/transfers/41'));
    expect(detail.request.method).toBe('GET');
    detail.flush({});
  });

  it('requests tenant destinations and server-side paginated history', () => {
    service.getTransferDestinations().subscribe();
    const destinations = http.expectOne(request => request.url.endsWith('/api/inventory/transfers/destinations'));
    expect(destinations.request.method).toBe('GET');
    destinations.flush([]);

    service.getTransfers({ page: 3, pageSize: 25, from: '2026-09-01', to: null, search: 'Bodega' }).subscribe();
    const history = http.expectOne(request => request.url.endsWith('/api/inventory/transfers'));
    expect(history.request.params.get('page')).toBe('3');
    expect(history.request.params.get('pageSize')).toBe('25');
    expect(history.request.params.get('from')).toBe('2026-09-01');
    expect(history.request.params.get('search')).toBe('Bodega');
    history.flush({ items: [], page: 3, pageSize: 25, totalItems: 53, totalPages: 3 });
  });
});
