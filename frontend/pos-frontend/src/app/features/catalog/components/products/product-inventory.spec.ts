import { TestBed } from '@angular/core/testing';
import { SimpleChange, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { ProductDialog } from './product-dialog';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';
import { Product, ProductEditorState } from '../../models/product.model';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { AuthStore } from '../../../../core/stores/auth.store';
import { PermissionService } from '../../../../core/services/permission.service';
import { PERMISSIONS } from '../../../../core/constants/permissions';

describe('Unified product editor', () => {
  const item: Product = { id: 71, name: 'Synthetic service', categoryId: 7, price: 10, cost: 0,
    minimumStock: 0, vatCategory: ProductVatCategory.Vat15, isActive: true, tracksInventory: true };
  const state = (product = item): ProductEditorState => ({ product, stateToken: 'STATE-1', productRevision: 2,
    classificationRevision: 4, classification: { revision: 4, declaredVatCategory: 2, kind: 1, activity: 1,
      taxBasis: 0, legalReference: 'Synthetic law', evidenceReference: 'Synthetic evidence', sriMaterialCode: null,
      naturalAgriculturalFoodConfirmed: false, foodProcessing: null, constructionMaterialConfirmed: false,
      confirmed: true, isCurrent: true, declaredByUserId: 1, declaredAt: '2026-09-18T20:00:00Z' },
    canDisableInventory: true, salePending: [] });
  const companyId = signal(1); const token = signal('synthetic-token');
  const permission = vi.fn((_code: string) => true);
  let http: HttpTestingController;

  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
    companyId.set(1); token.set('synthetic-token'); permission.mockReset().mockReturnValue(true);
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: CategoryService, useValue: { getAll: vi.fn(() => of([{ id: 7, name: 'Synthetic', isActive: true }])) } },
      { provide: AuthStore, useValue: { companyId, token, establishmentId: signal(1), emissionPointId: signal(1) } },
      { provide: PermissionService, useValue: { hasPermission: permission } },
    ] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  function open(product: Product | null = null) {
    const dialog = TestBed.runInInjectionContext(() => new ProductDialog());
    dialog.visible = true; dialog.product = product;
    dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });
    return dialog;
  }
  function load(dialog: ProductDialog, value = state()) {
    http.expectOne(r => r.url.endsWith('/editor/' + dialog.product?.id)).flush(value);
  }
  function basics(dialog: ProductDialog) {
    dialog.form.patchValue({ name: 'Synthetic copy', categoryId: 7, price: .10, cost: 0, minimumStock: 0 });
  }
  function confirmService(dialog: ProductDialog) {
    dialog.form.controls.kind.setValue(1);
    Object.assign(dialog.draft, { activity: 1, taxBasis: 0, legalReference: 'Synthetic law',
      evidenceReference: 'Synthetic evidence', confirmed: true });
  }
  function reopen(dialog: ProductDialog) {
    dialog.visible = false; dialog.ngOnChanges({ visible: new SimpleChange(true, false, false) });
    dialog.visible = true; dialog.ngOnChanges({ visible: new SimpleChange(false, true, false) });
  }

  it('starts with inventory, proposes service without inventory explicitly and never fabricates facts', () => {
    const dialog = open(); basics(dialog);
    expect(dialog.form.controls.tracksInventory.value).toBe(true);
    dialog.form.controls.kind.setValue(1);
    expect(dialog.form.controls.tracksInventory.value).toBe(false);
    expect(dialog.draft.activity).toBeNull(); expect(dialog.draft.taxBasis).toBeNull();
    expect(dialog.draft.evidenceReference).toBe(''); expect(dialog.draft.confirmed).toBe(false);
    dialog.save();
    expect(dialog.error).toContain('declaracion vigente');
    http.expectNone(r => r.method === 'POST');
    dialog.form.controls.tracksInventory.setValue(true);
    dialog.save();
    const basic = http.expectOne(r => r.method === 'POST');
    expect(basic.request.body.classification).toBeNull();
    expect(basic.request.body.tracksInventory).toBe(true);
    basic.flush(state({ ...item, name: 'Synthetic copy' }));
  });

  it('sends one atomic service request and ignores double click while pending', () => {
    const dialog = open(); basics(dialog); confirmService(dialog);
    dialog.section = 'inventory'; dialog.section = 'fiscal'; dialog.section = 'basic';
    expect(dialog.form.controls.name.value).toBe('Synthetic copy'); expect(dialog.draft.evidenceReference).toBe('Synthetic evidence');
    const saved = vi.fn(); dialog.saved.subscribe(saved);
    dialog.save(true); dialog.save(true); dialog.hide();
    expect(dialog.saving).toBe(true);
    const request = http.expectOne(r => r.url.endsWith('/Products/editor'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(expect.objectContaining({ tracksInventory: false, name: 'Synthetic copy',
      expectedStateToken: null, classification: expect.objectContaining({ kind: 1, confirmed: true,
        expectedRevision: 0, expectedProductRevision: 0 }) }));
    expect(request.request.body).not.toHaveProperty('isActive');
    request.flush(state({ ...item, tracksInventory: false }));
    expect(saved).toHaveBeenCalledTimes(1); expect(dialog.visible).toBe(true);
    expect(dialog.draft.confirmed).toBe(false);
    http.expectNone(r => r.method === 'POST');
  });

  it('loads coherent current product and revisions instead of combining stale table data with fresh classification', () => {
    const dialog = open(item);
    expect(dialog.loading).toBe(true); expect(dialog.canSave()).toBe(false);
    load(dialog, { ...state({ ...item, name: 'Current identity', price: 20 }), productRevision: 9, stateToken: 'CURRENT' });
    expect(dialog.form.controls.name.value).toBe('Current identity'); expect(dialog.form.controls.price.value).toBe(20);
    expect(dialog.reviewRequired).toBe(true);
    dialog.save(); http.expectNone(r => r.method === 'PUT');
    dialog.reviewed = true; confirmService(dialog); dialog.save(true);
    const request = http.expectOne(r => r.method === 'PUT');
    expect(request.request.body).toEqual(expect.objectContaining({ name: 'Current identity', price: 20, expectedStateToken: 'CURRENT',
      classification: expect.objectContaining({ expectedRevision: 4, expectedProductRevision: 9 }) }));
    request.flush(state({ ...item, name: 'Current identity', price: 20, tracksInventory: false }));
  });

  it('keeps drafts after known validation error and blocks stale revision retry until human reconciliation', () => {
    const dialog = open(item); load(dialog); confirmService(dialog);
    dialog.form.controls.name.setValue('My unsaved name'); dialog.save(true);
    http.expectOne(r => r.method === 'PUT').flush({ error: 'CATEGORY_INACTIVE' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.form.controls.name.value).toBe('My unsaved name'); expect(dialog.draft.confirmed).toBe(true);
    expect(dialog.canSave()).toBe(true);
    dialog.save(true);
    http.expectOne(r => r.method === 'PUT').flush({ error: 'PRODUCT_EDITOR_STATE_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.conflict).toBe(true); expect(dialog.form.controls.name.value).toBe('My unsaved name');
    dialog.save(true); http.expectNone(r => r.method === 'PUT');
  });

  it('treats a timeout as uncertain and never automatically repeats creation without identifiers', () => {
    const dialog = open(); basics(dialog); dialog.save();
    const request = http.expectOne(r => r.method === 'POST');
    expect(request.request.body.internalCode).toBeNull();
    request.error(new ProgressEvent('error'));
    expect(dialog.uncertain).toBe(true); expect(dialog.error).toContain('no repitas');
    expect(dialog.form.controls.name.value).toBe('Synthetic copy');
    dialog.save(); http.expectNone(r => r.method === 'POST');
  });

  it('continues a basic save on the same ID via PUT and keeps unfinished fiscal fields', () => {
    const dialog = open(); basics(dialog);
    dialog.draft.legalReference = 'Synthetic draft law';
    dialog.save(); http.expectOne(r => r.method === 'POST').flush({ ...state(), classification: null, classificationRevision: 0 });
    expect(dialog.draft.legalReference).toBe('Synthetic draft law');
    confirmService(dialog); dialog.save(true);
    const request = http.expectOne(r => r.url.endsWith('/editor/71') && r.method === 'PUT');
    request.flush(state({ ...item, tracksInventory: false }));
    http.expectNone(r => r.method === 'POST');
  });

  it('restores the service draft and unlocks inventory after basic save without a classification', () => {
    const dialog = open(); basics(dialog); confirmService(dialog);
    dialog.form.controls.tracksInventory.setValue(true);
    dialog.save(); http.expectOne(r => r.method === 'POST').flush({ ...state(), classification: null, classificationRevision: 0, canDisableInventory: false });
    expect(dialog.form.controls.kind.value).toBe(1);
    expect(dialog.form.controls.tracksInventory.enabled).toBe(true);
    dialog.form.controls.tracksInventory.setValue(false);
    dialog.draft.confirmed = true; dialog.save(true);
    const request = http.expectOne(r => r.method === 'PUT');
    expect(request.request.body.tracksInventory).toBe(false); expect(request.request.body.classification.kind).toBe(1);
    request.flush(state({ ...item, tracksInventory: false }));
  });

  it('reconciles stale state without losing the draft or silently overwriting concurrent price/cost changes', () => {
    const dialog = open(item); load(dialog); confirmService(dialog);
    dialog.form.controls.name.setValue('My local rename'); dialog.form.controls.price.setValue(12);
    dialog.save(true); http.expectOne(r => r.method === 'PUT').flush({ error: 'PRODUCT_EDITOR_STATE_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    dialog.reviewCurrent();
    const current = { ...state({ ...item, price: 20, cost: 7 }), stateToken: 'CURRENT-2', productRevision: 3, classificationRevision: 5 };
    http.expectOne(r => r.method === 'GET').flush(current);
    expect(dialog.form.controls.name.value).toBe('My local rename');
    expect(dialog.reconciliation.find(row => row.field === 'price')?.choice).toBe('current');
    expect(dialog.reconciliation.find(row => row.field === 'name')?.choice).toBe('draft');
    dialog.fiscalChoice = 'draft'; dialog.reconcile();
    expect(dialog.form.controls.name.value).toBe('My local rename'); expect(dialog.form.controls.price.value).toBe(20);
    expect(dialog.form.controls.cost.value).toBe(7); expect(dialog.draft.evidenceReference).toBe('Synthetic evidence');
    expect(dialog.draft.confirmed).toBe(false);
    dialog.draft.confirmed = true; dialog.save(true);
    const put = http.expectOne(r => r.method === 'PUT');
    expect(put.request.body).toEqual(expect.objectContaining({ expectedStateToken: 'CURRENT-2', price: 20, cost: 7,
      classification: expect.objectContaining({ expectedProductRevision: 3, expectedRevision: 5 }) }));
    put.flush(current);
  });

  it('reconciles an uncertain creation to a human-selected existing record instead of repeating POST', () => {
    const dialog = open(); basics(dialog); dialog.save();
    http.expectOne(r => r.method === 'POST').error(new ProgressEvent('error'));
    dialog.findUncertainCreation(); const lookup = http.expectOne(r => r.url.endsWith('/lookup'));
    expect(lookup.request.params.get('take')).toBe('30'); lookup.flush([item]);
    expect(dialog.form.controls.name.value).toBe('Synthetic copy');
    dialog.reviewCurrent(item.id); http.expectOne(r => r.url.endsWith('/editor/71')).flush(state());
    dialog.reconcile(); dialog.save();
    const put = http.expectOne(r => r.method === 'PUT'); expect(put.request.url).toContain('/editor/71'); put.flush(state());
    http.expectNone(r => r.method === 'POST');
  });

  it('ignores late reconciliation and lookup responses after token/context changes', () => {
    const dialog = open(item); load(dialog); dialog.save();
    http.expectOne(r => r.method === 'PUT').flush({ error: 'PRODUCT_EDITOR_STATE_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    dialog.reviewCurrent(); const get = http.expectOne(r => r.method === 'GET');
    token.set('rotated'); get.flush(state({ ...item, name: 'Late reconciliation' }));
    expect(dialog.comparison).toBeNull(); expect(dialog.form.controls.name.value).toBe(item.name);
    expect(dialog.canSave()).toBe(false);
  });

  it('ignores stale loads and errors when the same ID closes and reopens', () => {
    const dialog = open(item);
    const old = http.expectOne(r => r.method === 'GET');
    reopen(dialog);
    const latest = http.expectOne(r => r.method === 'GET');
    latest.flush(state({ ...item, name: 'Reopened current' }));
    old.flush({ error: 'OLD_ERROR' }, { status: 400, statusText: 'Bad Request' });
    expect(dialog.form.controls.name.value).toBe('Reopened current'); expect(dialog.error).toBe('');
    expect(dialog.loading).toBe(false);
  });

  it('ignores late success from another product and late save from an old dialog generation', () => {
    const dialog = open(item);
    const old = http.expectOne(r => r.method === 'GET');
    dialog.product = { ...item, id: 72 }; dialog.ngOnChanges({ product: new SimpleChange(item, dialog.product, false) });
    load(dialog, state(dialog.product)); old.flush(state({ ...item, name: 'Wrong late product' }));
    expect(dialog.form.controls.name.value).toBe(item.name);
    const saved = vi.fn(); dialog.saved.subscribe(saved);
    dialog.save(); const saving = http.expectOne(r => r.method === 'PUT');
    reopen(dialog); load(dialog, state(dialog.product));
    saving.flush(state({ ...item, name: 'Old save response' }));
    expect(dialog.form.controls.name.value).toBe(item.name); expect(saved).not.toHaveBeenCalled();
  });

  it.each(['load', 'save', 'error'] as const)('guards %s callbacks and submit after tenant or token changes', phase => {
    const dialog = open(item); const get = http.expectOne(r => r.method === 'GET');
    if (phase === 'load') {
      companyId.set(2); get.flush(state({ ...item, name: 'Wrong tenant' }));
      expect(dialog.form.controls.name.value).toBe('');
    } else {
      get.flush(state()); dialog.save(); const put = http.expectOne(r => r.method === 'PUT');
      token.set('synthetic-token-rotated');
      if (phase === 'save') put.flush(state({ ...item, name: 'Wrong session' }));
      else put.flush({ error: 'OLD_ERROR' }, { status: 409, statusText: 'Conflict' });
      expect(dialog.form.controls.name.value).toBe(item.name); expect(dialog.error).toBe('');
    }
    expect(dialog.canSave()).toBe(false);
    dialog.save(); http.expectNone(r => r.method !== 'GET');
  });

  it('ignores categories returned after close or context switch', () => {
    const categories = new Subject<{ id: number; name: string; isActive: boolean }[]>();
    vi.spyOn(TestBed.inject(CategoryService), 'getAll').mockReturnValue(categories);
    const dialog = open(); companyId.set(2);
    categories.next([{ id: 99, name: 'Wrong tenant category', isActive: true }]);
    expect(dialog.categories()).toEqual([]);
  });

  it('supports basic catalog-only permission without declaring fiscal facts', () => {
    permission.mockImplementation(code => code === PERMISSIONS.catalogProductsWrite || code === PERMISSIONS.catalogProductsRead);
    const dialog = open(); basics(dialog);
    expect(dialog.form.controls.kind.disabled).toBe(true);
    dialog.save(true); http.expectNone(r => r.method === 'POST');
    dialog.save(); const request = http.expectOne(r => r.method === 'POST');
    expect(request.request.body.classification).toBeNull(); request.flush({ ...state(), classification: null });
  });

  it.each([false, true])('marks manual cost intervention only when the cost control was explicitly edited: %s', manual => {
    const dialog = open(item); load(dialog); confirmService(dialog);
    if (manual) dialog.form.controls.cost.markAsDirty();
    dialog.save(true); const request = http.expectOne(r => r.method === 'PUT');
    expect(request.request.body.cost).toBe(item.cost);
    expect(request.request.body.setManualCost).toBe(manual);
    request.flush(state({ ...item, tracksInventory: false }));
    expect(dialog.form.controls.cost.pristine).toBe(true);
  });

  it('keeps explicit same-value manual cost intent through reconciliation only when the draft cost is selected', () => {
    const dialog = open(item); load(dialog); dialog.form.controls.cost.markAsDirty();
    dialog.save(); http.expectOne(r => r.method === 'PUT').flush({ error: 'PRODUCT_EDITOR_STATE_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    dialog.reviewCurrent(); http.expectOne(r => r.method === 'GET').flush({ ...state(), stateToken: 'REVIEWED' });
    dialog.reconcile(); dialog.save(); const request = http.expectOne(r => r.method === 'PUT');
    expect(request.request.body.setManualCost).toBe(true); request.flush(state());
  });

  it('renders the accessible inventory switch and keeps tracking false in the raw save payload', () => {
    const fixture = TestBed.createComponent(ProductDialog);
    fixture.componentRef.setInput('visible', true); fixture.componentRef.setInput('product', item); fixture.detectChanges();
    load(fixture.componentInstance); fixture.componentInstance.section = 'inventory'; fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('#product-tracks-inventory') as HTMLInputElement;
    expect(input.type).toBe('checkbox'); expect(input.getAttribute('aria-label')).toBe('Controla inventario');
    input.click(); fixture.detectChanges();
    expect(fixture.componentInstance.form.getRawValue().tracksInventory).toBe(false);
    fixture.componentInstance.save();
    const request = http.expectOne(r => r.method === 'PUT');
    expect(request.request.body.tracksInventory).toBe(false); request.flush(state({ ...item, tracksInventory: false }));
  });

  it('omits inventory and revision from legacy updates without weakening the existing endpoint', () => {
    TestBed.inject(ProductService).update(71, { categoryId: 7, name: 'Synthetic legacy', price: 10, cost: 0,
      minimumStock: 0, vatCategory: ProductVatCategory.Vat15 }).subscribe();
    const request = http.expectOne(r => r.url.endsWith('/Products/71'));
    expect(request.request.body).not.toHaveProperty('tracksInventory');
    expect(request.request.body).not.toHaveProperty('isActive'); request.flush(null);
  });
});
