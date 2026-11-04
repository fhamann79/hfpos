import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ProductService } from '../../services/product.service';
import { ProductsTable } from './products-table';
import { ProductDialog } from './product-dialog';
import { SimpleChange } from '@angular/core';
import { DEFAULT_VAT_CATEGORY } from '../../../../core/utils/vat-category';
import { CategoryService } from '../../services/category.service';
import { PermissionService } from '../../../../core/services/permission.service';

describe('Product lifecycle', () => {
  const item = { id: 1, categoryId: 1, name: 'Producto histórico', price: 10, cost: 4, minimumStock: 3, vatCategory: DEFAULT_VAT_CATEGORY, isActive: true };
  const page = (items = [item]) => ({ items, page: 1, pageSize: 10, totalItems: items.length, totalPages: items.length ? 1 : 0 });

  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
    TestBed.configureTestingModule({
      imports: [ProductsTable, ProductDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }),
        ConfirmationService, MessageService, { provide: PermissionService, useValue: { hasPermission: () => true } }],
    });
    vi.spyOn(TestBed.inject(CategoryService), 'getAll').mockReturnValue(of([{ id: 1, name: 'Categoría', isActive: true }]));
    vi.spyOn(TestBed.inject(ProductService), 'editor').mockReturnValue(of({ product: item,
      stateToken: 'SYNTHETIC', productRevision: 0, classificationRevision: 0,
      classification: null, canDisableInventory: false, salePending: [] }));
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('renders Desactivar for active records and Activar for inactive records, without delete', async () => {
    vi.spyOn(TestBed.inject(ProductService), 'getPage').mockReturnValue(of(page([item, { ...item, id: 2, isActive: false }])));
    const fixture = TestBed.createComponent(ProductsTable);
    fixture.componentRef.setInput('canWrite', true);

    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const rows = element.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('Desactivar');
    expect(rows[1].textContent).toContain('Activar');
    expect(element.querySelector('.pi-trash')).toBeNull();
    expect(TestBed.inject(ProductService)).not.toHaveProperty('delete');
  });

  it('confirms lifecycle, posts explicit endpoints and refreshes the table', () => {
    const service = TestBed.inject(ProductService);
    const reload = vi.spyOn(service, 'getPage').mockReturnValue(of(page()));
    const fixture = TestBed.createComponent(ProductsTable);
    fixture.componentRef.setInput('canWrite', true);

    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const requests = TestBed.inject(HttpTestingController);
    for (const active of [true, false]) {
      fixture.componentInstance.confirmLifecycle({ ...item, isActive: active });
      expect(confirm.mock.lastCall?.[0].acceptLabel).toBe(active ? 'Desactivar' : 'Activar');
      confirm.mock.lastCall?.[0].accept?.();
      const request = requests.expectOne((req) => req.url.endsWith(`/api/Products/1/${active ? 'deactivate' : 'activate'}`));
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({});
      request.flush(null);
    }
    expect(reload).toHaveBeenCalledTimes(2);
  });

  it('does not offer lifecycle mutations without write permission', () => {
    const fixture = TestBed.createComponent(ProductsTable);
    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    fixture.componentInstance.confirmLifecycle(item);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('keeps activation out of the edit form and emitted HTTP payload', () => {
    const dialog = TestBed.runInInjectionContext(() => new ProductDialog());
    dialog.product = item;
    dialog.visible = true;
    dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });
    dialog.save();
    expect(dialog.form.controls).not.toHaveProperty('isActive');
    const request = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'PUT');
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush({ product: item, stateToken: 'SYNTHETIC-2', productRevision: 0, classificationRevision: 0,
      classification: null, canDisableInventory: false, salePending: [] });
  });

  it('keeps only the inactive product current category alongside active categories', () => {
    vi.mocked(TestBed.inject(CategoryService).getAll).mockReturnValue(of([
      { id: 1, name: 'Histórica', isActive: false },
      { id: 2, name: 'Otra inactiva', isActive: false },
      { id: 3, name: 'Activa', isActive: true },
    ]));
    vi.mocked(TestBed.inject(ProductService).editor).mockReturnValue(of({ product: { ...item, isActive: false },
      stateToken: 'SYNTHETIC', productRevision: 0, classificationRevision: 0,
      classification: null, canDisableInventory: false, salePending: [] }));
    const fixture = TestBed.createComponent(ProductDialog);
    fixture.componentRef.setInput('product', { ...item, isActive: false });
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(fixture.componentInstance.availableCategories().map((category) => category.id)).toEqual([1, 3]);
    expect(fixture.componentInstance.form.controls.categoryId.value).toBe(1);
    expect(TestBed.inject(ProductService).editor).toHaveBeenCalledWith(item.id);
  });

  it('reports a failed lifecycle change and preserves the loaded records', () => {
    const fixture = TestBed.createComponent(ProductsTable);
    fixture.componentRef.setInput('canWrite', true);

    const confirm = vi.spyOn(TestBed.inject(ConfirmationService), 'confirm');
    const toast = vi.spyOn(TestBed.inject(MessageService), 'add');
    fixture.componentInstance.confirmLifecycle(item);
    confirm.mock.lastCall?.[0].accept?.();
    TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'POST')
      .flush({ error: 'PRODUCT_ALREADY_INACTIVE' }, { status: 409, statusText: 'Conflict' });
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });
});
