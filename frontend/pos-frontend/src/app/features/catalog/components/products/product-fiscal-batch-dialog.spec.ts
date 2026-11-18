import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductVatCategory, VAT_CATEGORY_OPTIONS } from '../../../../core/utils/vat-category';
import { FiscalBatchCandidate, FiscalBatchPreview } from '../../services/product-fiscal-batch.service';
import { ProductFiscalBatchDialog } from './product-fiscal-batch-dialog';

describe('Product fiscal batch dialog', () => {
  const companyId = signal(5);
  const operationId = '28e24050-d1c0-4be2-ab96-e96320654ab3';
  const allowed = vi.fn((_permission: string) => true);
  let fixture: ComponentFixture<ProductFiscalBatchDialog>;
  let dialog: ProductFiscalBatchDialog;
  let http: HttpTestingController;

  const product = (id: number): FiscalBatchCandidate => ({
    id, name: `Synthetic item ${id}`, internalCode: `SYN-${id}`, barcode: null, categoryId: 7,
    isActive: true, vatCategory: ProductVatCategory.Vat0, fiscalStatus: 'pending',
  });
  const candidates = (items = [product(1), product(2)], totalItems = items.length) => ({ items, totalItems, maxProducts: 250 });
  const endpoint = (action: string) => http.expectOne(r => r.url.endsWith('/fiscal-batches/' + action));
  const host = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => [...host().querySelectorAll('button')].find(b => b.textContent?.trim() === label)!;

  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
    companyId.set(5);
    allowed.mockReset().mockReturnValue(true);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthStore, useValue: { companyId, establishmentId: signal(3), emissionPointId: signal(7), token: signal('synthetic') } },
        { provide: PermissionService, useValue: { hasPermission: allowed } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProductFiscalBatchDialog);
    fixture.componentRef.setInput('categories', []);
    dialog = fixture.componentInstance;
    fixture.detectChanges();
    endpoint('candidates').flush(candidates());
  });
  afterEach(() => { http.verify(); vi.unstubAllGlobals(); });

  function declare() {
    dialog.editDeclaration();
    Object.assign(dialog.draft, { kind: 0, activity: 0, taxBasis: 0, legalReference: 'Synthetic law',
      evidenceReference: 'Synthetic evidence', confirmed: true });
    dialog.reason = 'Synthetic reason';
    fixture.detectChanges();
  }

  function report(token: string | null, rejected = 0): FiscalBatchPreview {
    return { operationId, companyId: 5, companyName: 'Synthetic company', fiscalDate: '2026-09-18',
      reason: dialog.reason, declaration: { ...dialog.draft }, rows: [], changed: rejected ? 0 : 2,
      unchanged: 0, rejected, previewToken: token, expiresAt: null };
  }

  it('selects all explicitly, supports exceptions and clear, and sends only checked ids to preview', async () => {
    await fixture.whenStable();
    const selectAll = () => host().querySelector<HTMLInputElement>('thead input[type=checkbox]')!;
    const rows = () => [...host().querySelectorAll<HTMLInputElement>('tbody input[type=checkbox]')];
    expect(rows().map(row => row.checked)).toEqual([false, false]);
    selectAll().click();
    await fixture.whenStable();
    expect(selectAll().checked).toBe(true);
    expect(rows().map(row => row.checked)).toEqual([true, true]);
    expect(host().textContent).toContain('2 encontrados / 2 seleccionados');
    rows()[0].click();
    await fixture.whenStable();
    expect(selectAll().checked).toBe(false);
    expect(selectAll().indeterminate).toBe(true);
    selectAll().click();
    await fixture.whenStable();
    expect(rows().map(row => row.checked)).toEqual([true, true]);
    button('Limpiar selección').click();
    await fixture.whenStable();
    expect(selectAll().checked || selectAll().indeterminate).toBe(false);
    expect(rows().map(row => row.checked)).toEqual([false, false]);
    rows()[1].click();
    declare();
    dialog.review();
    const preview = endpoint('preview');
    expect(preview.request.body.productIds).toEqual([2]);
    preview.flush(report(null, 1));
    dialog.confirm();
    http.expectNone(r => r.url.endsWith('/confirm'));
  });

  it('invalidates visible rows and selection on filter edit and cancels a late candidate response', async () => {
    await fixture.whenStable();
    dialog.toggle(1, true);
    dialog.draft.confirmed = true;
    const vat = host().querySelector<HTMLSelectElement>('select[name=vat]')!;
    expect([...vat.options].slice(1).map(option => option.text)).toEqual(VAT_CATEGORY_OPTIONS.map(option => option.label));
    vat.selectedIndex = 1;
    vat.dispatchEvent(new Event('change'));
    expect(dialog.selected.size).toBe(0);
    expect(dialog.draft.confirmed).toBe(false);
    expect(dialog.candidates).toEqual([]);
    expect(dialog.total).toBeNull();
    dialog.search();
    const older = endpoint('candidates');
    expect(older.request.params.get('vatCategory')).toBe('0');
    const search = host().querySelector<HTMLInputElement>('input[name=search]')!;
    search.value = 'SYN-2';
    search.dispatchEvent(new Event('input'));
    expect(older.cancelled).toBe(true);
    expect(dialog.loading).toBe(false);
    dialog.search();
    const current = endpoint('candidates');
    expect(current.request.params.get('search')).toBe('SYN-2');
    expect(current.request.params.get('vatCategory')).toBe('0');
    expect(current.request.params.has('page')).toBe(false);
    current.flush(candidates([product(2)]));
    await fixture.whenStable();
    expect(host().textContent).toContain('1 encontrados / 0 seleccionados');
    expect(host().textContent).not.toContain('Synthetic item 1');
  });

  it('renders all 250 rows, no selectable rows at 251, and distinct empty and error states', async () => {
    dialog.search();
    endpoint('candidates').flush(candidates(Array.from({ length: 250 }, (_, i) => product(i + 1))));
    await fixture.whenStable();
    expect(host().querySelectorAll('tbody input[type=checkbox]')).toHaveLength(250);
    dialog.toggleAll(true);
    expect(dialog.selected.size).toBe(250);
    dialog.search();
    endpoint('candidates').flush(candidates([], 251));
    await fixture.whenStable();
    expect(host().textContent).toContain('251 encontrados / 0 seleccionados');
    expect(host().textContent).toContain('Acota los filtros a 250 productos o menos.');
    expect(host().querySelector('input[type=checkbox]')).toBeNull();
    expect(button('Continuar').disabled).toBe(true);
    dialog.search();
    endpoint('candidates').flush(candidates([]));
    await fixture.whenStable();
    expect(host().textContent).toContain('Sin resultados');
    dialog.search();
    endpoint('candidates').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(host().textContent).toContain('No se pudieron consultar los productos.');
    expect(host().textContent).not.toContain('Sin resultados');
  });

  it('clears the attestation after changing the selected products or material fiscal facts', async () => {
    dialog.toggle(1, true);
    declare();
    await fixture.whenStable();
    expect(document.activeElement).toBe((fixture.nativeElement as HTMLElement).querySelector('[data-batch-focus]'));
    expect(dialog.canPreview()).toBe(true);
    const evidence = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('app-product-fiscal-fields input:not([type=checkbox])')[1];
    evidence.value = 'Different evidence';
    evidence.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(dialog.draft.confirmed).toBe(false);
    expect(dialog.canPreview()).toBe(false);
    dialog.draft.confirmed = true;
    dialog.editSelection();
    dialog.toggle(2, true);
    expect(dialog.draft.confirmed).toBe(false);
  });

  it('suppresses double confirmation, retries the same token after uncertainty and invalidates on conflict', () => {
    dialog.toggle(1, true);
    declare();
    dialog.review();
    endpoint('preview').flush(report('server-proof'));
    dialog.confirm();
    dialog.confirm();
    const first = endpoint('confirm');
    expect(first.request.body).toEqual({ operationId, previewToken: 'server-proof', confirmed: true });
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    dialog.confirm();
    const retry = endpoint('confirm');
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({}, { status: 409, statusText: 'Conflict' });
    expect(dialog.preview).toBeNull();
    expect(dialog.stage).toBe('declaration');
  });

  it('renders HTTP preview and receipt in the zoneless view without a manual change detection pass', async () => {
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Synthetic item 1');
    dialog.toggle(1, true);
    declare();
    dialog.review();
    const preview = report('server-proof');
    endpoint('preview').flush(preview);
    await fixture.whenStable();
    expect(host.textContent).toContain('Revisar clasificación fiscal');
    expect(document.activeElement).toBe(host.querySelector('[data-batch-focus]'));
    dialog.confirm();
    endpoint('confirm').flush({ ...preview, userId: 9, appliedAt: '2026-09-18T20:00:00Z', profileRevision: 2 });
    await fixture.whenStable();
    expect(host.textContent).toContain('Comprobante fiscal');
    expect(host.textContent).toContain(operationId);
  });

  it('ignores a late preview after tenant scope changes and requires fiscal permission', () => {
    dialog.toggle(1, true);
    declare();
    allowed.mockImplementation(permission => permission !== PERMISSIONS.fiscalSettingsWrite);
    dialog.review();
    http.expectNone(r => r.url.endsWith('/preview'));
    allowed.mockReturnValue(true);
    dialog.review();
    const response = endpoint('preview');
    companyId.set(6);
    response.flush(report('server-proof'));
    expect(dialog.preview).toBeNull();
  });

  it('opens recent receipt detail without a UUID and announces clipboard success and failure', async () => {
    const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('Denied'));
    vi.stubGlobal('navigator', new Proxy(navigator, {
      get: (target, property) => property === 'clipboard' ? { writeText } : Reflect.get(target, property, target),
    }));
    const history = host().querySelector('details')!;
    history.open = true;
    // Dispatch explicitly because jsdom queues the native toggle on a separate timer.
    history.dispatchEvent(new Event('toggle'));
    const appliedAt = '2026-09-18T20:00:00Z';
    endpoint('recent').flush([{ operationId, appliedAt, rowCount: 2 }]);
    await fixture.whenStable();
    expect(history.textContent).not.toContain(operationId);
    history.querySelector<HTMLButtonElement>('.recent button')!.click();
    endpoint(operationId).flush({ ...report(null), userId: 9, appliedAt, profileRevision: 2 });
    await fixture.whenStable();
    expect(host().textContent).toContain('Comprobante fiscal');
    button('Copiar referencia').click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith(operationId);
    expect(host().querySelector('[role=status]')?.textContent).toContain('Referencia copiada.');
    button('Copiar referencia').click();
    await fixture.whenStable();
    expect(host().querySelector('[role=status]')?.textContent).toContain('No se pudo copiar.');
  });

  it('handles recent error and empty results while keeping manual UUID recovery available', async () => {
    dialog.loadRecent();
    endpoint('recent').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(host().textContent).toContain('No se pudieron consultar los comprobantes recientes.');
    button('Reintentar').click();
    endpoint('recent').flush([]);
    await fixture.whenStable();
    expect(host().textContent).toContain('No hay comprobantes recientes');
    dialog.lookupId = ` ${operationId} `;
    dialog.recover();
    endpoint(operationId).flush({ ...report(null), userId: 9, appliedAt: '2026-09-18T20:00:00Z', profileRevision: 2 });
    expect(dialog.stage).toBe('result');
  });
});
