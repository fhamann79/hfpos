import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../core/services/permission.service';
import { InitialDataPage } from './initial-data-page';
import { InitialDataService } from './initial-data.service';
import { InitialDataPreview, InitialDataResult, TenantReadiness } from './initial-data.model';

const preview: InitialDataPreview = { requestId: '', kind: 'categories', duplicatePolicy: 'create-only',
  atomicity: 'atomic-batch', maxRows: 500, canConfirm: true, previewToken: 'protected-token', errors: [],
  rows: [{ rowNumber: 2, values: { name: 'Category' }, errors: [], resolvedId: null }] };
const readiness: TenantReadiness = { companyId: 1, establishmentId: 2, emissionPointId: 3, ready: false, checks: [] };
const result: InitialDataResult = { batchId: 1, requestId: '', kind: 'categories', companyId: 1, establishmentId: 2,
  emissionPointId: 3, userId: 4, rowCount: 1, createdIds: [8], rowNumbers: [2], createdAt: '2026-10-04T05:00:00Z' };

describe('Initial business data workflow', () => {
  let page: InitialDataPage;
  let api: { preview: ReturnType<typeof vi.fn>; confirm: ReturnType<typeof vi.fn>; readiness: ReturnType<typeof vi.fn>;
    batches: ReturnType<typeof vi.fn>; template: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    api = { preview: vi.fn(() => of(preview)), confirm: vi.fn(() => of(result)), readiness: vi.fn(() => of(readiness)),
      batches: vi.fn(() => of([])), template: vi.fn() };
    TestBed.configureTestingModule({ imports: [InitialDataPage], providers: [provideRouter([]), providePrimeNG({ unstyled: true }), { provide: InitialDataService, useValue: api },
      { provide: PermissionService, useValue: { hasAllPermissions: () => true } }] });
    page = TestBed.runInInjectionContext(() => new InitialDataPage()); page.ngOnInit();
  });
  afterEach(() => page.ngOnDestroy());
  async function file(csv = 'name\nCategory\n') {
    const input = { files: [{ name: 'test.csv', size: csv.length, text: () => Promise.resolve(csv) }], value: 'test.csv' };
    await page.selectFile({ target: input } as unknown as Event);
    expect(input.value).toBe('');
  }
  it('preview never confirms automatically and passes explicit create-only policy', async () => {
    await file(); page.showPreview();
    expect(api.preview).toHaveBeenCalledWith(expect.objectContaining({ kind: 'categories', duplicatePolicy: 'create-only', csv: 'name\nCategory\n' }));
    expect(api.confirm).not.toHaveBeenCalled(); expect(page.preview()?.canConfirm).toBe(true);
  });
  it('retains exact batch intent after ambiguous failure and blocks double confirmation', async () => {
    await file(); page.showPreview();
    const pending = new Subject<InitialDataResult>(); api.confirm.mockReturnValueOnce(pending);
    page.confirm(); page.confirm(); expect(api.confirm).toHaveBeenCalledTimes(1);
    const payload = api.confirm.mock.calls[0][0];
    pending.error(new HttpErrorResponse({ status: 0 }));
    expect(page.saving()).toBe(false);
    page.confirm(); expect(api.confirm.mock.calls[1][0]).toEqual(payload);
    expect(api.confirm.mock.calls[1][1]).toBe('protected-token'); expect(page.result()?.batchId).toBe(1);
  });
  it('invalid preview blocks confirmation and a new file gets a new UUID', async () => {
    await file(); api.preview.mockReturnValue(of({ ...preview, canConfirm: false, previewToken: null })); page.showPreview(); page.confirm();
    expect(api.confirm).not.toHaveBeenCalled(); const first = api.preview.mock.calls[0][0].requestId;
    await file('name\nOther\n'); page.showPreview();
    expect(api.preview.mock.calls[1][0].requestId).not.toBe(first);
  });
  it('ignores stale previews after destroy and stale file reads after dataset changes', async () => {
    await file(); const pending = new Subject<InitialDataPreview>(); api.preview.mockReturnValueOnce(pending);
    page.showPreview(); page.ngOnDestroy(); pending.next(preview); expect(page.preview()).toBeNull();
  });
  it('confirmation failures leave the reviewed payload available without partial-success claims', async () => {
    await file(); page.showPreview(); api.confirm.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409,
      error: { error: 'INITIAL_DATA_REVALIDATION_FAILED' } })));
    page.confirm(); expect(page.result()).toBeNull(); expect(page.preview()).not.toBeNull(); expect(page.error()).not.toBe('');
  });
  it('renders physical CSV row to created ID mapping in result and history, including opening movements', async () => {
    const fixture = TestBed.createComponent(InitialDataPage);
    fixture.detectChanges();
    const audited = { ...result, rowCount: 2, rowNumbers: [2, 5], createdIds: [8, 19] };
    fixture.componentInstance.result.set(audited);
    fixture.componentInstance.batches.set([{ ...audited, kind: 'opening-inventory', batchId: 2 }]);
    fixture.detectChanges(); await fixture.whenStable();
    const text = (selector: string) => Array.from((fixture.nativeElement as HTMLElement).querySelectorAll(selector))
      .map(node => node.textContent?.replace(/\s+/g, ' ').trim());
    expect(text('[data-testid="result-row-audit"]')).toEqual(['Fila 2: ID #8', 'Fila 5: ID #19']);
    expect(text('[data-testid="history-row-audit"]')).toEqual(['Fila 2: Movimiento #8', 'Fila 5: Movimiento #19']);
    expect(fixture.componentInstance.rowAudit(audited)).toEqual([{ rowNumber: 2, createdId: 8 }, { rowNumber: 5, createdId: 19 }]);
  });
});

describe('Initial data typed HTTP contract', () => {
  it('uses the tenant API without accepting company or establishment overrides', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(InitialDataService); const http = TestBed.inject(HttpTestingController);
    const payload = { requestId: 'synthetic-uuid', kind: 'categories' as const, csv: 'name\nC', duplicatePolicy: 'create-only' as const };
    api.confirm(payload, 'token').subscribe(response => {
      expect(response.rowNumbers).toEqual([2]); expect(response.createdIds).toEqual([8]);
    });
    const request = http.expectOne(r => r.url.endsWith('/api/initial-data/confirm'));
    expect(request.request.body).toEqual({ payload, previewToken: 'token' }); request.flush(result); http.verify();
  });
});
