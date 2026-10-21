import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../core/stores/auth.store';
import { PermissionService } from '../../../core/services/permission.service';
import { FiscalProfile, FiscalRegime, ProductFiscalClassification, ProductFiscalState, TaxpayerKind } from '../models/fiscal-profile.model';
import { FiscalProfileService } from '../services/fiscal-profile.service';
import { FiscalProfilePanel } from './fiscal-profile-panel';
import { ProductFiscalDialog } from './product-fiscal-dialog';

describe('Explicit tenant fiscal declarations', () => {
  const company = signal(1);
  let writable: boolean;
  let histories: Subject<FiscalProfile[]>[];
  let products: Subject<ProductFiscalState>[];
  let declared: Subject<FiscalProfile>;
  let classified: Subject<ProductFiscalClassification>;
  const api = { history: vi.fn(), declare: vi.fn(), product: vi.fn(), declareProduct: vi.fn() };
  const row = (revision = 1): FiscalProfile => ({
    id: revision, revision, regime: FiscalRegime.General, taxpayerKind: TaxpayerKind.Natural,
    isAccountingRequired: false, specialTaxpayerResolution: null, withholdingAgentResolution: null,
    effectiveFrom: '2024-04-01', effectiveUntil: null, evidenceReference: 'Synthetic evidence', reason: 'Synthetic declaration',
    confirmed: true, rimpeEligibilityConfirmed: false, declaredByUserId: 10, declaredAt: '2026-10-20T12:00:00Z',
  });
  const productRow = (revision = 1): ProductFiscalClassification => ({
    revision, kind: 0, activity: 0, taxBasis: 0, legalReference: 'LRTI 65', evidenceReference: 'Synthetic product evidence',
    sriMaterialCode: null, naturalAgriculturalFoodConfirmed: false, foodProcessing: null,
    constructionMaterialConfirmed: false, confirmed: true, declaredVatCategory: 2,
    declaredByUserId: 10, declaredAt: '2026-10-20T12:00:00Z', isCurrent: true,
  });
  const productState = (productRevision: number, classification: ProductFiscalClassification | null): ProductFiscalState => ({
    productRevision, classification, name: 'Current synthetic identity', internalCode: 'CURRENT', barcode: '9999999999999', vatCategory: 0,
  });
  beforeEach(() => {
    vi.clearAllMocks(); company.set(1); writable = true; histories = []; products = [];
    declared = new Subject(); classified = new Subject();
    api.history.mockImplementation(() => { const next = new Subject<FiscalProfile[]>(); histories.push(next); return next; });
    api.product.mockImplementation(() => { const next = new Subject<ProductFiscalState>(); products.push(next); return next; });
    api.declare.mockReturnValue(declared); api.declareProduct.mockReturnValue(classified);
    TestBed.configureTestingModule({ providers: [
      { provide: FiscalProfileService, useValue: api },
      { provide: PermissionService, useValue: { hasPermission: () => writable } },
      { provide: AuthStore, useValue: { companyId: company, establishmentId: () => 2, emissionPointId: () => 3, token: () => 'synthetic-only' } },
    ] });
  });
  afterEach(() => TestBed.resetTestingModule());

  it('requires an explicit declaration and permission rather than defaulting a regime', () => {
    const panel = TestBed.runInInjectionContext(() => new FiscalProfilePanel()); TestBed.tick(); histories[0].next([]);
    expect(panel.draft.regime).toBeNull(); expect(panel.draft.confirmed).toBe(false);
    panel.save(); expect(api.declare).not.toHaveBeenCalled(); expect(panel.error()).toContain('confirma');
    panel.draft = { ...panel.draft, regime: FiscalRegime.General, taxpayerKind: TaxpayerKind.Natural,
      reason: 'Synthetic change', evidenceReference: 'Synthetic reference', confirmed: true };
    writable = false; panel.save(); expect(api.declare).not.toHaveBeenCalled();
    writable = true; panel.save(); expect(api.declare).toHaveBeenCalledOnce();
    expect(api.declare.mock.calls[0][0].expectedRevision).toBe(0);
    declared.error(new HttpErrorResponse({ status: 409, error: { error: 'FISCAL_PROFILE_REVISION_CONFLICT' } }));
    expect(panel.saving()).toBe(false); expect(panel.error()).toContain('Otro usuario');
  });

  it('discards old tenant history and cannot submit before the new tenant is loaded', () => {
    const panel = TestBed.runInInjectionContext(() => new FiscalProfilePanel()); TestBed.tick();
    company.set(2); histories[0].next([row(99)]); expect(panel.history()).toEqual([]);
    panel.save(); expect(api.declare).not.toHaveBeenCalled(); TestBed.tick();
    histories[1].next([row(2)]); expect(panel.draft.expectedRevision).toBe(2);
    expect(panel.draft.regime).toBeNull(); expect(panel.draft.confirmed).toBe(false);
    histories[0].next([row(99)]); expect(panel.draft.expectedRevision).toBe(2);
  });

  it('requires renewed product confirmation, respects permissions and displays conflicts', () => {
    const dialog = TestBed.runInInjectionContext(() => new ProductFiscalDialog());
    dialog.product = { id: 7, name: 'Synthetic product' }; dialog.visible = true; dialog.ngOnChanges();
    products[0].next(productState(4, productRow(3)));
    expect(dialog.draft.expectedRevision).toBe(3); expect(dialog.draft.expectedProductRevision).toBe(4); expect(dialog.draft.confirmed).toBe(false);
    dialog.save(); expect(api.declareProduct).not.toHaveBeenCalled();
    dialog.draft.confirmed = true; writable = false; dialog.save(); expect(api.declareProduct).not.toHaveBeenCalled();
    writable = true; dialog.save(); expect(api.declareProduct).toHaveBeenCalledWith(7, expect.objectContaining({ expectedRevision: 3, expectedProductRevision: 4, confirmed: true }));
    classified.error(new HttpErrorResponse({ status: 409, error: { error: 'FISCAL_CLASSIFICATION_CONFLICT' } }));
    expect(dialog.saving).toBe(false); expect(dialog.error).toContain('categoría IVA');
  });

  it('ignores prior product responses and denies stale tenant submissions', () => {
    const dialog = TestBed.runInInjectionContext(() => new ProductFiscalDialog());
    dialog.product = { id: 7, name: 'First synthetic product' }; dialog.visible = true; dialog.ngOnChanges();
    dialog.product = { id: 8, name: 'Second synthetic product' }; dialog.ngOnChanges();
    products[0].next(productState(90, productRow(90))); expect(dialog.loading).toBe(true);
    products[1].next(productState(2, null)); expect(dialog.draft.expectedRevision).toBe(0); expect(dialog.current).toBe(false);
    expect(dialog.draft.expectedProductRevision).toBe(2);
    dialog.draft.confirmed = true; company.set(2); dialog.save(); expect(api.declareProduct).not.toHaveBeenCalled();
    products[0].next(productState(90, productRow(90))); expect(dialog.draft.expectedRevision).toBe(0);
  });

  it.each([null, productRow(1)])('shows the current identity when the catalog was stale before GET (%s)', classification => {
    const dialog = TestBed.runInInjectionContext(() => new ProductFiscalDialog());
    dialog.product = { id: 7, name: 'Obsolete catalog identity', vatCategory: 2 }; dialog.visible = true; dialog.ngOnChanges();
    expect(dialog.canSave()).toBe(false);
    products[0].next(productState(5, classification));
    expect(dialog.identity?.name).toBe('Current synthetic identity'); expect(dialog.identity?.vatCategory).toBe(0);
    expect(dialog.draft.expectedProductRevision).toBe(5); expect(dialog.draft.confirmed).toBe(false);
    dialog.draft.confirmed = true; dialog.save();
    expect(api.declareProduct).toHaveBeenCalledWith(7, expect.objectContaining({ expectedProductRevision: 5 }));
  });
});
