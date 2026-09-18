import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ProductService } from '../../services/product.service';
import { ProductsTable } from './products-table';
import { ProductDialog, ProductDialogSubmit } from './product-dialog';
import { DEFAULT_VAT_CATEGORY } from '../../../../core/utils/vat-category';
import { CategoryService } from '../../services/category.service';

describe('Product lifecycle', () => {
  const item = { id: 1, categoryId: 1, name: 'Producto histórico', price: 10, cost: 4, minimumStock: 3, vatCategory: DEFAULT_VAT_CATEGORY, isActive: true };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProductsTable, ProductDialog],
      providers: [provideHttpClient(), provideHttpClientTesting(), providePrimeNG({ unstyled: true }),
        ConfirmationService, MessageService],
    });
    vi.spyOn(TestBed.inject(CategoryService), 'getAll').mockReturnValue(of([{ id: 1, name: 'Categoría', isActive: true }]));
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('renders Desactivar for active records and Activar for inactive records, without delete', async () => {
    vi.spyOn(TestBed.inject(ProductService), 'getAll').mockReturnValue(of([item, { ...item, id: 2, isActive: false }]));
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
    const reload = vi.spyOn(service, 'getAll').mockReturnValue(of([item]));
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

    dialog.form.patchValue({ ...item });
    const events: ProductDialogSubmit[] = [];
    dialog.submitForm.subscribe((event) => events.push(event));
    dialog.save();
    expect(events).toHaveLength(1);
    expect(events[0].mode).toBe('edit');
    expect(events[0].payload).not.toHaveProperty('isActive');
    expect(dialog.form.controls).not.toHaveProperty('isActive');
    const payload = { ...item };
    TestBed.inject(ProductService).update(item.id, payload).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne((req) => req.method === 'PUT');
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush(null);
  });

  it('keeps only the inactive product current category alongside active categories', () => {
    vi.mocked(TestBed.inject(CategoryService).getAll).mockReturnValue(of([
      { id: 1, name: 'Histórica', isActive: false },
      { id: 2, name: 'Otra inactiva', isActive: false },
      { id: 3, name: 'Activa', isActive: true },
    ]));
    const fixture = TestBed.createComponent(ProductDialog);
    fixture.componentRef.setInput('product', { ...item, isActive: false });
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(fixture.componentInstance.categories().map((category) => category.id)).toEqual([1, 3]);
    expect(fixture.componentInstance.form.controls.categoryId.value).toBe(1);
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
