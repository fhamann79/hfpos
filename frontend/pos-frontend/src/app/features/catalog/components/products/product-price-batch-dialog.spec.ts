import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { FiscalBatchCandidate, PriceBatchPreview, PriceBatchResult } from '../../services/product-price-batch.service';
import { ProductPriceBatchDialog } from './product-price-batch-dialog';

describe('Product price batch dialog', () => {
  const companyId = signal(5);
  const operationId = '28e24050-d1c0-4be2-ab96-e96320654ab3';
  const allowed = vi.fn((_permission: string) => true);
  let fixture: ComponentFixture<ProductPriceBatchDialog>;
  let dialog: ProductPriceBatchDialog;
  let http: HttpTestingController;
  const product = (id: number): FiscalBatchCandidate => ({ id, name: `Synthetic item ${id}`, internalCode: `SYN-${id}`,
    barcode: null, categoryId: 7, isActive: true, vatCategory: ProductVatCategory.Vat0, fiscalStatus: 'pending' });
  const candidates = (count = 2) => ({ totalItems: count, maxProducts: 250,
    items: count <= 250 ? Array.from({ length: count }, (_, i) => product(i + 1)) : [] });
  const endpoint = (action: string) => http.expectOne(r => r.url.endsWith('/price-batches/' + action));
  const host = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => [...host().querySelectorAll('button')].find(b => b.textContent?.trim() === label)!;

  function mount() {
    fixture = TestBed.createComponent(ProductPriceBatchDialog);
    fixture.componentRef.setInput('categories', []);
    dialog = fixture.componentInstance;
    fixture.detectChanges();
  }
  beforeEach(() => {
    sessionStorage.clear();
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
    companyId.set(5); allowed.mockReset().mockReturnValue(true);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthStore, useValue: { companyId, me: signal({ userId: '9' }), establishmentId: signal(3),
        emissionPointId: signal(7), token: signal('synthetic') } },
      { provide: PermissionService, useValue: { hasPermission: allowed } }] });
    http = TestBed.inject(HttpTestingController); mount(); endpoint('candidates').flush(candidates());
  });
  afterEach(() => { http.verify({ ignoreCancelled: true }); sessionStorage.clear(); vi.unstubAllGlobals(); });

  function report(token: string | null = 'synthetic-proof', rejected = 0): PriceBatchPreview {
    return { operationId, companyId: 5, companyName: 'Synthetic company', reason: 'Synthetic reason', action: 'increase-percent', value: 10,
      rows: [{ productId: 1, name: 'Synthetic item 1', internalCode: 'SYN-1', currentPrice: 10, cost: 2, proposedPrice: rejected ? null : 11,
        currentMargin: '80', proposedMargin: '81.8182', outcome: rejected ? 'rejected' : 'changed', rejection: rejected ? 'GROSS_MARGIN_ZERO_COST' : null,
        warnings: rejected ? ['ZERO_COST'] : [] }], changed: rejected ? 0 : 1, unchanged: 0, rejected,
      previewToken: token, expiresAt: '2026-11-18T20:00:00Z' };
  }
  const result = (): PriceBatchResult => ({ ...report(), appliedAt: '2026-11-18T19:00:00Z', userId: 9 });
  function review() {
    dialog.toggle(1, true); dialog.editDeclaration(); dialog.value = 10; dialog.reason = 'Synthetic reason';
    dialog.review(); endpoint('preview').flush(report()); fixture.detectChanges();
  }
  function uncertain() {
    review(); dialog.acknowledged = true; dialog.confirm();
    const request = endpoint('confirm'); const body: unknown = request.request.body;
    request.flush({ error: 'SYNTHETIC_RESPONSE_LOSS' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges(); return body;
  }

  it('selects all 228 complete results, excludes exceptions and sends only explicit IDs', async () => {
    dialog.search(); endpoint('candidates').flush(candidates(228)); await fixture.whenStable();
    host().querySelector<HTMLInputElement>('thead input[type=checkbox]')!.click();
    await fixture.whenStable(); expect(dialog.selected.size).toBe(228);
    dialog.toggle(2, false); dialog.toggle(228, false);
    dialog.editDeclaration(); dialog.reason = 'Synthetic bulk'; dialog.action = 'decrease-amount'; dialog.value = 1.005;
    dialog.review(); const request = endpoint('preview');
    expect(request.request.body.productIds).toHaveLength(226);
    expect(request.request.body.productIds).not.toContain(2); expect(request.request.body.productIds).not.toContain(228);
    expect(request.request.body).toMatchObject({ action: 'decrease-amount', value: 1.005 });
    request.flush(report()); expect(dialog.preview?.previewToken).toBe('synthetic-proof');
  });

  it('shows exact overflow count without partial rows and retains all five AND query filters', () => {
    dialog.filters = { search: 'SYN', active: false, categoryId: 7, fiscalStatus: 'current', vatCategory: ProductVatCategory.Vat0 };
    dialog.search(); const request = endpoint('candidates');
    expect(request.request.params.get('search')).toBe('SYN'); expect(request.request.params.get('active')).toBe('false');
    expect(request.request.params.get('categoryId')).toBe('7'); expect(request.request.params.get('fiscalStatus')).toBe('current');
    expect(request.request.params.get('vatCategory')).toBe(String(ProductVatCategory.Vat0));
    request.flush(candidates(251)); fixture.detectChanges();
    expect(dialog.total).toBe(251); expect(dialog.candidates).toEqual([]);
    expect(host().textContent).toContain('251 encontrados / 0 seleccionados');
  });

  it('filter edit and clear cancel late candidate/preview responses and invalidate proof', () => {
    dialog.search(); const late = endpoint('candidates'); dialog.filtersChanged(); expect(late.cancelled).toBe(true);
    expect(dialog.total).toBeNull(); expect(dialog.selected.size).toBe(0);
    dialog.search(); endpoint('candidates').flush(candidates());
    dialog.toggle(1, true); dialog.reason = 'Synthetic'; dialog.review(); const preview = endpoint('preview');
    dialog.clearSelection(); preview.flush(report()); expect(dialog.preview).toBeNull(); expect(dialog.busy).toBe(false);
    dialog.search(); const emptySelectionLate = endpoint('candidates'); dialog.clearSelection(); expect(emptySelectionLate.cancelled).toBe(true);
    expect(dialog.loading).toBe(false);
  });

  it('shows realized gross margin, known-zero rejection and requires explicit confirmation', () => {
    review(); expect(host().textContent).toContain('81.8182%');
    dialog.confirm(); http.expectNone(r => r.url.endsWith('/confirm'));
    dialog.editDeclaration(); dialog.action = 'gross-margin'; dialog.value = 100;
    expect(dialog.canPreview()).toBe(false); dialog.value = 20; expect(dialog.canPreview()).toBe(true);
    dialog.review(); endpoint('preview').flush(report(null, 1)); fixture.detectChanges();
    expect(host().textContent).toContain('Costo cero registrado'); expect(host().textContent).not.toContain('desconocido');
    dialog.acknowledged = true; dialog.confirm(); http.expectNone(r => r.url.endsWith('/confirm'));
  });

  it('persists proof before HTTP and blocks edits/new previews while the confirmation outcome is uncertain', () => {
    const first = uncertain(); expect(sessionStorage.length).toBe(1); expect(dialog.pending).toBe(true);
    dialog.editSelection(); dialog.editDeclaration(); dialog.clearSelection(); dialog.filtersChanged(); dialog.exclude(1);
    dialog.review(); dialog.search(); expect(dialog.preview?.operationId).toBe(operationId);
    http.expectNone(r => r.url.endsWith('/preview') || r.url.endsWith('/candidates'));
    dialog.confirm(); const retry = endpoint('confirm'); expect(retry.request.body).toEqual(first);
    retry.flush(result()); expect(dialog.pending).toBe(false); expect(sessionStorage.length).toBe(0);
  });

  it('close/reopen restores the same operation and receipt lookup resolves a lost response', () => {
    uncertain(); dialog.close(); fixture.destroy(); mount();
    http.expectNone(r => r.url.endsWith('/candidates')); expect(dialog.pending).toBe(true);
    expect(dialog.preview?.operationId).toBe(operationId); expect(dialog.stage).toBe('preview');
    dialog.review(); http.expectNone(r => r.url.endsWith('/preview'));
    dialog.recover(operationId); endpoint(operationId).flush(result());
    expect(dialog.result?.operationId).toBe(operationId); expect(sessionStorage.length).toBe(0);
  });

  it('a network failure keeps proof and 404 receipt lookup does not clear uncertainty', () => {
    review(); dialog.acknowledged = true; dialog.confirm(); endpoint('confirm').error(new ProgressEvent('error'));
    dialog.recover(operationId); endpoint(operationId).flush({ error: 'NOT_FOUND' }, { status: 404, statusText: 'Not found' });
    expect(dialog.pending).toBe(true); expect(sessionStorage.length).toBe(1); expect(dialog.canPreview()).toBe(false);
  });

  it('only an explicit safe 409 clears pending proof, not an operation conflict', () => {
    uncertain(); dialog.confirm(); endpoint('confirm').flush({ error: 'PRICE_BATCH_REQUEST_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.pending).toBe(true); dialog.confirm();
    endpoint('confirm').flush({ error: 'PRICE_BATCH_PREVIEW_CHANGED' }, { status: 409, statusText: 'Conflict' });
    expect(dialog.pending).toBe(false); expect(sessionStorage.length).toBe(0); expect(dialog.preview).toBeNull();
  });

  it('does not send HTTP if pending storage fails and fails closed for corrupt recovery', () => {
    review(); const save = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Synthetic storage failure'); });
    dialog.acknowledged = true; dialog.confirm(); http.expectNone(r => r.url.endsWith('/confirm')); expect(dialog.recoveryBlocked).toBe(true);
    save.mockRestore(); dialog.close(); fixture.destroy(); sessionStorage.setItem('hfpos-price-batch-v1:5:9', '{invalid'); mount();
    expect(dialog.recoveryBlocked).toBe(true); http.expectNone(r => r.url.endsWith('/candidates'));
  });

  it('requires new batch permission but never fiscal permissions and closes on tenant changes', () => {
    allowed.mockImplementation(code => ![PERMISSIONS.fiscalSettingsRead, PERMISSIONS.fiscalSettingsWrite].includes(code as typeof PERMISSIONS.fiscalSettingsRead));
    review(); expect(dialog.preview?.previewToken).toBeTruthy();
    const close = vi.spyOn(dialog.closed, 'emit'); companyId.set(6); fixture.detectChanges(); expect(close).toHaveBeenCalled();
    dialog.acknowledged = true; dialog.confirm(); http.expectNone(r => r.url.endsWith('/confirm'));
  });

  it('loads recent receipts, recovers by reference and copies the stable reference', async () => {
    dialog.loadRecent(); endpoint('recent').flush([{ operationId, appliedAt: result().appliedAt, rowCount: 1 }]);
    dialog.recover(operationId); endpoint(operationId).flush(result());
    const write = vi.fn().mockResolvedValue(undefined); vi.stubGlobal('navigator', { clipboard: { writeText: write } });
    await dialog.copyReference(dialog.result!); expect(write).toHaveBeenCalledWith(operationId); expect(dialog.copyStatus).toContain('copiada');
  });

  it('shows exact margin strings and enforces the batch amount and rule precision bounds', () => {
    dialog.toggle(1, true); dialog.reason = 'Synthetic precision'; dialog.action = 'set-price'; dialog.value = 1000000000;
    expect(dialog.canPreview()).toBe(true); dialog.value = 1000000000.01; expect(dialog.canPreview()).toBe(false);
    dialog.value = 1.23451; expect(dialog.canPreview()).toBe(false);
    dialog.value = 1.2345; dialog.review(); const request = endpoint('preview');
    expect(request.request.body.value).toBe(1.2345);
    const exact = report(); exact.value = 1.2345; exact.rows[0].currentMargin = '-9999999999899.1234';
    request.flush(exact); fixture.detectChanges();
    expect(host().textContent).toContain('-9999999999899.1234%'); expect(host().textContent).toContain('1.2345');
    dialog.editDeclaration(); dialog.review(); const unsafe = report(null, 1);
    Object.assign(unsafe.rows[0], { currentPrice: null, cost: null, currentMargin: null, proposedPrice: null,
      proposedMargin: null, rejection: 'PRICE_BATCH_AMOUNT_LIMIT' });
    endpoint('preview').flush(unsafe); fixture.detectChanges();
    expect(host().textContent).toContain('Valores omitidos para evitar perdida de precision');
    expect(host().textContent).not.toContain('90071992547409');
  });
});
