import { TestBed } from '@angular/core/testing';
import { SimpleChange } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ProductDialog } from './product-dialog';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';
import { FiscalProfileService } from '../../../fiscal-settings/services/fiscal-profile.service';
import { ProductVatCategory } from '../../../../core/utils/vat-category';

describe('Catalog inventory mode', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [
    provideHttpClient(), provideHttpClientTesting(),
    { provide: CategoryService, useValue: { getAll: () => of([]) } },
    { provide: FiscalProfileService, useValue: { product: () => of({ classification: {
      revision: 4, kind: 1, confirmed: true, isCurrent: true,
    } }) } },
  ] }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('defaults new products to tracking and preserves a saved false value with expected classification revision', () => {
    const dialog = TestBed.runInInjectionContext(() => new ProductDialog());
    dialog.visible = true; dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });
    expect(dialog.form.controls.tracksInventory.value).toBe(true);
    dialog.product = { id: 71, name: 'Synthetic service', categoryId: 7, price: 10, cost: 0,
      minimumStock: 0, vatCategory: ProductVatCategory.Vat15, isActive: true, tracksInventory: false };
    dialog.ngOnChanges({ product: new SimpleChange(null, dialog.product, false) });
    expect(dialog.form.controls.tracksInventory.value).toBe(false);
    const submit = vi.fn(); dialog.submitForm.subscribe(submit); dialog.save();
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ mode: 'edit', payload: expect.objectContaining({
      tracksInventory: false, expectedClassificationRevision: 4,
    }) }));
  });

  it('sends the unchecked dialog mode and expected revision through the actual HTTP service', () => {
    const dialog = TestBed.runInInjectionContext(() => new ProductDialog());
    dialog.visible = true;
    dialog.product = { id: 71, name: 'Synthetic service', categoryId: 7, price: 10, cost: 0,
      minimumStock: 0, vatCategory: ProductVatCategory.Vat15, isActive: true, tracksInventory: true };
    dialog.ngOnChanges({ product: new SimpleChange(null, dialog.product, true) });
    dialog.form.controls.tracksInventory.setValue(false);
    const service = TestBed.inject(ProductService);
    dialog.submitForm.subscribe(event => {
      if (event.mode === 'edit') service.update(event.id, event.payload).subscribe();
    });
    dialog.save();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url.endsWith('/api/Products/71'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(expect.objectContaining({ tracksInventory: false, expectedClassificationRevision: 4 }));
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush(null);
  });

  it('omits inventory mode and revision from legacy updates so the backend preserves the saved mode', () => {
    TestBed.inject(ProductService).update(71, { categoryId: 7, name: 'Synthetic edited service',
      price: 10, cost: 0, minimumStock: 0, vatCategory: ProductVatCategory.Vat15 }).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(r => r.url.endsWith('/api/Products/71'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).not.toHaveProperty('tracksInventory');
    expect(request.request.body).not.toHaveProperty('expectedClassificationRevision');
    request.flush(null);
  });
});
