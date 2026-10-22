import { TestBed } from '@angular/core/testing';
import { SimpleChange } from '@angular/core';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ProductDialog } from './product-dialog';
import { CategoryService } from '../../services/category.service';
import { FiscalProfileService } from '../../../fiscal-settings/services/fiscal-profile.service';
import { ProductVatCategory } from '../../../../core/utils/vat-category';

describe('Catalog inventory mode', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [
    { provide: CategoryService, useValue: { getAll: () => of([]) } },
    { provide: FiscalProfileService, useValue: { product: () => of({ classification: {
      revision: 4, kind: 1, confirmed: true, isCurrent: true,
    } }) } },
  ] }));

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
});
