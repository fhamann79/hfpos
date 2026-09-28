import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';
import { ProductsTable } from './products-table';

describe('ProductsTable pagination requests', () => {
  it('ignores an older response that arrives after the latest request', () => {
    const firstRequest = new Subject<any>();
    const secondRequest = new Subject<any>();
    const getPage = vi.fn()
      .mockReturnValueOnce(firstRequest)
      .mockReturnValueOnce(secondRequest);

    TestBed.configureTestingModule({
      providers: [
        MessageService,
        ConfirmationService,
        { provide: ProductService, useValue: { getPage } },
        { provide: CategoryService, useValue: { getAll: vi.fn() } },
      ],
    });

    const table = TestBed.runInInjectionContext(() => new ProductsTable());
    table.loadProducts(1, 10);
    table.globalFilter = 'nuevo';
    table.loadProducts(2, 10);

    secondRequest.next({
      items: [product(2, 'Nuevo producto')],
      page: 2,
      pageSize: 10,
      totalItems: 12,
      totalPages: 2,
    });
    firstRequest.next({
      items: [product(1, 'Producto obsoleto')],
      page: 1,
      pageSize: 10,
      totalItems: 12,
      totalPages: 2,
    });

    expect(table.products().map((item) => item.name)).toEqual(['Nuevo producto']);
    expect(table.currentPage()).toBe(2);
    expect(table.first).toBe(10);
    expect(table.loading()).toBe(false);
  });
});

function product(id: number, name: string) {
  return {
    id,
    categoryId: 1,
    name,
    barcode: null,
    internalCode: null,
    price: 10,
    cost: 4,
    minimumStock: 0,
    vatCategory: 0,
    isActive: true,
  };
}
