import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductVatCategory } from '../../../core/utils/vat-category';
import { PosProductCatalogService } from './pos-product-catalog.service';

describe('PosProductCatalogService remote stock lookup', () => {
  let service: PosProductCatalogService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PosProductCatalogService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('searches the integrated bounded POS endpoint and maps product stock', () => {
    service.searchProducts('ABC-123', 30).subscribe((products) => {
      expect(products).toEqual([
        expect.objectContaining({
          id: 7,
          name: 'Producto remoto',
          barcode: 'ABC-123',
          internalCode: 'INT-7',
          price: 4.5,
          stock: 8,
          vatCategory: ProductVatCategory.Vat15,
          isActive: true,
        }),
      ]);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Inventory/pos-products'));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('search')).toBe('ABC-123');
    expect(request.request.params.get('take')).toBe('30');
    request.flush([
      {
        id: 7,
        name: 'Producto remoto',
        barcode: 'ABC-123',
        internalCode: 'INT-7',
        price: 4.5,
        stock: 8,
        vatCategory: ProductVatCategory.Vat15,
        isActive: true,
      },
    ]);
  });

  it('uses only a bounded integrated lookup as the workstation availability probe', () => {
    service.getProductsWithStock().subscribe((snapshot) => {
      expect(snapshot.inventoryAvailable).toBe(true);
      expect(snapshot.products).toEqual([]);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Inventory/pos-products'));
    expect(request.request.params.get('take')).toBe('1');
    expect(request.request.params.has('search')).toBe(false);
    request.flush([{ id: 1, name: 'Probe', price: 1, stock: 1, vatCategory: 2, isActive: true }]);
  });

  it('marks inventory unavailable when the integrated lookup cannot be reached', () => {
    service.getProductsWithStock().subscribe((snapshot) => {
      expect(snapshot.inventoryAvailable).toBe(false);
      expect(snapshot.products).toEqual([]);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Inventory/pos-products'));
    request.flush({ error: 'offline' }, { status: 503, statusText: 'Unavailable' });
  });
});
