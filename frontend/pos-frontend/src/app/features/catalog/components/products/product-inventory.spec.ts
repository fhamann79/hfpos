import { TestBed } from '@angular/core/testing';
import { SimpleChange } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { ProductDialog } from './product-dialog';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';
import { FiscalProfileService } from '../../../fiscal-settings/services/fiscal-profile.service';
import { ProductFiscalClassification, ProductFiscalState } from '../../../fiscal-settings/models/fiscal-profile.model';
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

  function renderInventoryControl(tracksInventory = true) {
    const fixture = TestBed.createComponent(ProductDialog);
    fixture.componentRef.setInput('product', {
      id: 71, name: 'Synthetic service', categoryId: 7, price: 10, cost: 0,
      minimumStock: 0, vatCategory: ProductVatCategory.Vat15, isActive: true, tracksInventory,
    });
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('#product-tracks-inventory') as HTMLInputElement;
    return { fixture, dialog: fixture.componentInstance, input };
  }

  it('renders a focusable native switch with its visible state and accessible label', () => {
    const { fixture, dialog, input } = renderInventoryControl();
    const label = fixture.nativeElement.querySelector('label[for="product-tracks-inventory"]') as HTMLLabelElement;
    const track = input.nextElementSibling as HTMLElement;
    expect({
      tag: input.tagName, type: input.type, id: input.id,
      name: input.getAttribute('aria-label'), label: label.textContent?.trim(),
      disabled: input.disabled, checked: input.checked, tabIndex: input.tabIndex,
      track: track.className, state: track.nextElementSibling?.textContent?.trim(),
    }).toMatchInlineSnapshot(`
      {
        "checked": true,
        "disabled": false,
        "id": "product-tracks-inventory",
        "label": "Controla inventario",
        "name": "Controla inventario",
        "state": "Sí",
        "tabIndex": 0,
        "tag": "INPUT",
        "track": "app-boolean-switch__track",
        "type": "checkbox",
      }
    `);
    input.focus();
    expect(document.activeElement).toBe(input);
    // jsdom has no native Space activation; ensure the browser default is not intercepted.
    for (const type of ['keydown', 'keyup']) {
      const space = new KeyboardEvent(type, { key: ' ', code: 'Space', bubbles: true, cancelable: true });
      expect(input.dispatchEvent(space)).toBe(true);
      expect(space.defaultPrevented).toBe(false);
    }
    input.click();
    fixture.detectChanges();
    expect(dialog.form.controls.tracksInventory.value).toBe(false);
    expect(track.nextElementSibling?.textContent?.trim()).toBe('No');
    label.click();
    fixture.detectChanges();
    expect(input.checked).toBe(true);
    expect(dialog.form.controls.tracksInventory.value).toBe(true);
    expect(fixture.nativeElement.querySelector('p-checkbox')).toBeNull();
  });

  it.each([
    ['missing', null],
    ['goods', { kind: 0, confirmed: true, isCurrent: true }],
    ['unconfirmed', { kind: 1, confirmed: false, isCurrent: true }],
    ['outdated', { kind: 1, confirmed: true, isCurrent: false }],
  ] as const)('blocks disabling inventory for %s classification while preserving the raw payload', (_, classification) => {
    vi.spyOn(TestBed.inject(FiscalProfileService), 'product').mockReturnValue(of({
      classification: classification ? { ...classification, revision: 4 } : null,
    } as ProductFiscalState));
    const { fixture, dialog, input } = renderInventoryControl();
    expect(input.disabled).toBe(true);
    expect(dialog.form.controls.tracksInventory.disabled).toBe(true);
    input.click();
    (fixture.nativeElement.querySelector('label[for="product-tracks-inventory"]') as HTMLLabelElement).click();
    expect(input.checked).toBe(true);
    expect(dialog.form.controls.tracksInventory.value).toBe(true);
    const submit = vi.fn();
    dialog.submitForm.subscribe(submit);
    dialog.save();
    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ payload: expect.objectContaining({
      tracksInventory: true, expectedClassificationRevision: classification ? 4 : undefined,
    }) }));
  });

  it('always allows enabling inventory, then blocks disabling it without a confirmed current Service', () => {
    vi.spyOn(TestBed.inject(FiscalProfileService), 'product').mockReturnValue(of({ classification: null } as ProductFiscalState));
    const { fixture, dialog, input } = renderInventoryControl(false);
    expect(input.disabled).toBe(false);
    expect(input.checked).toBe(false);
    input.click();
    fixture.detectChanges();
    expect(input.checked).toBe(true);
    expect(input.disabled).toBe(true);
    input.click();
    expect(dialog.form.controls.tracksInventory.value).toBe(true);
  });

  it('blocks while classification is loading and unlocks only after current Service confirmation', () => {
    const state = new Subject<ProductFiscalState>();
    vi.spyOn(TestBed.inject(FiscalProfileService), 'product').mockReturnValue(state);
    const { fixture, input } = renderInventoryControl();
    expect(input.disabled).toBe(true);
    state.next({ classification: { revision: 4, kind: 1, confirmed: true, isCurrent: true } as ProductFiscalClassification } as ProductFiscalState);
    fixture.detectChanges();
    expect(input.disabled).toBe(false);
    state.error(new Error('Synthetic classification load failure'));
    fixture.detectChanges();
    expect(input.disabled).toBe(true);
    expect(input.checked).toBe(true);
  });
});
