import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
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
    isActive: true, vatCategory: 2, fiscalStatus: 'pending',
  });
  const page = (n: number) => ({ items: [product(n)], page: n, pageSize: 1, totalItems: 2, totalPages: 2 });
  const endpoint = (action: string) => http.expectOne(r => r.url.endsWith('/fiscal-batches/' + action));

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
    endpoint('candidates').flush(page(1));
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

  it('keeps explicit selections across server pages and ignores a late filtered response', () => {
    dialog.toggle(product(1), true);
    dialog.load(2);
    const older = endpoint('candidates');
    dialog.filters.fiscalStatus = 'pending';
    dialog.search();
    const newer = endpoint('candidates');
    expect(newer.request.params.get('fiscalStatus')).toBe('pending');
    newer.flush(page(1));
    older.flush(page(2));
    expect(dialog.candidates.map(p => p.id)).toEqual([1]);
    dialog.load(2);
    const next = endpoint('candidates');
    expect(next.request.params.get('page')).toBe('2');
    next.flush(page(2));
    dialog.toggle(product(2), true);
    declare();
    dialog.review();
    const preview = endpoint('preview');
    expect(preview.request.body.productIds).toEqual([1, 2]);
    preview.flush(report(null, 1));
    dialog.confirm();
    http.expectNone(r => r.url.endsWith('/confirm'));
  });

  it('clears the attestation after changing the selected products or material fiscal facts', async () => {
    dialog.toggle(product(1), true);
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
    dialog.toggle(product(2), true);
    expect(dialog.draft.confirmed).toBe(false);
  });

  it('suppresses double confirmation, retries the same token after uncertainty and invalidates on conflict', () => {
    dialog.toggle(product(1), true);
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
    dialog.toggle(product(1), true);
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
    dialog.toggle(product(1), true);
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
});
