import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { Dialog } from 'primeng/dialog';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PlatformTenants } from './platform-tenants';
import { PlatformApi } from './platform-api.service';
import { PlatformStore } from './platform.store';
import { PlatformPage, ProvisionResult, Tenant, TenantDetail, TenantEvent } from './platform.model';

const tenant: Tenant = { id: 1, name: 'Synthetic Tenant', ruc: '1790000000001', timeZoneId: 'America/Guayaquil', isActive: true, createdAt: '2026-09-30T12:00:00Z' };
const detail: TenantDetail = { company: tenant, establishmentCount: 1, activeEstablishmentCount: 1, emissionPointCount: 1,
  activeEmissionPointCount: 1, userCount: 1, activeUserCount: 1, events: [] };
const emptyEvents = { items: [], totalItems: 0, page: 1, pageSize: 10, totalPages: 0 };
const failure = new HttpErrorResponse({ status: 409, error: { error: 'TENANT_RUC_ALREADY_EXISTS' } });
const otherTenant: Tenant = { ...tenant, id: 2, name: 'Other Synthetic Tenant', ruc: '1790000000002' };
const otherDetail: TenantDetail = { ...detail, company: otherTenant };
const tenantEvent: TenantEvent = { id: 1, eventType: 'Provisioned', platformUsername: 'synthetic-platform', reason: null, createdAt: tenant.createdAt };
function tenantPage(company: Tenant, totalItems = 1): PlatformPage<Tenant> {
  return { items: [company], totalItems, page: 1, pageSize: 20, totalPages: 1 };
}
function eventPage(id: number, totalItems = 1): PlatformPage<TenantEvent> {
  return { items: [{ ...tenantEvent, id }], totalItems, page: 1, pageSize: 10, totalPages: 1 };
}
type ReadScope = 'list' | 'detail' | 'events';
type ReadResult = PlatformPage<Tenant> | TenantDetail | PlatformPage<TenantEvent>;

describe('Platform tenant workflows', () => {
  let component: PlatformTenants;
  let api: { tenants: ReturnType<typeof vi.fn>; provision: ReturnType<typeof vi.fn>; detail: ReturnType<typeof vi.fn>;
    events: ReturnType<typeof vi.fn>; setActive: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    api = { tenants: vi.fn(() => of({ items: [tenant], totalItems: 42, page: 1, pageSize: 20, totalPages: 3 })),
      provision: vi.fn(), detail: vi.fn(() => of(detail)), events: vi.fn(() => of(emptyEvents)), setActive: vi.fn() };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: PlatformApi, useValue: api },
      { provide: PlatformStore, useValue: { me: () => ({ username: 'synthetic-platform' }), clear: vi.fn() } }] });
    component = TestBed.runInInjectionContext(() => new PlatformTenants()); component.ngOnInit();
  });
  afterEach(() => component.ngOnDestroy());
  function startRead(scope: ReadScope, pending: Subject<ReadResult>) {
    if (scope === 'list') { api.tenants.mockReturnValue(pending); component.load(); }
    if (scope === 'detail') { api.detail.mockReturnValue(pending); component.openDetail(tenant); }
    if (scope === 'events') {
      api.events.mockReturnValue(pending);
      if (component.detail()) component.loadEvents(); else component.openDetail(tenant);
    }
  }
  function readLoading(scope: ReadScope) {
    return scope === 'list' ? component.loading() : scope === 'detail' ? component.detailLoading() : component.eventLoading();
  }
  function validForm() {
    component.openProvision(); component.form.patchValue({ name: tenant.name, ruc: tenant.ruc, username: 'synthetic-admin',
      email: 'synthetic@test.local', password: 'synthetic-password-only' });
  }
  it('initial load, filters and pagination send only one server-side page', () => {
    expect(component.tenants()).toHaveLength(1); expect(component.total()).toBe(42);
    expect(api.tenants).toHaveBeenCalledWith('', null, 1, 20);
    component.search = 'synthetic'; component.status = 'Suspended'; component.first = 40; component.filter();
    expect(api.tenants).toHaveBeenLastCalledWith('synthetic', 'Suspended', 1, 20);
    component.page({ first: 50, rows: 25 }); expect(api.tenants).toHaveBeenLastCalledWith('synthetic', 'Suspended', 3, 25);
  });
  it('form uses operational defaults and blocks invalid RUC/email/password', () => {
    component.openProvision(); expect(component.form.getRawValue()).toMatchObject({ timeZoneId: 'America/Guayaquil', establishment: 'Matriz', emissionPoint: 'Caja Principal' });
    component.provision(); expect(api.provision).not.toHaveBeenCalled();
    validForm(); component.form.patchValue({ email: 'invalid', ruc: '123', password: 'short' });
    component.provision(); expect(api.provision).not.toHaveBeenCalled(); expect(component.error()).toContain('Revisa');
  });
  it('keeps RequestId on retry and blocks double clicks', () => {
    validForm(); const pending = new Subject<ProvisionResult>(); api.provision.mockReturnValue(pending);
    component.provision(); component.provision(); expect(api.provision).toHaveBeenCalledTimes(1);
    const request = api.provision.mock.calls[0][0]; expect(request.requestId).toMatch(/^[0-9a-f-]{36}$/);
    pending.error(failure); expect(component.saving()).toBe(false); expect(component.error()).toContain('RUC');
    api.provision.mockReturnValue(throwError(() => failure)); component.provision();
    expect(api.provision.mock.calls[1][0].requestId).toBe(request.requestId);
  });
  it.each(['name', 'ruc', 'timeZoneId', 'establishment', 'address', 'emissionPoint', 'username', 'email', 'password'] as const)
    ('regenerates RequestId when draft field %s changes', field => {
      validForm(); api.provision.mockReturnValue(throwError(() => failure)); component.provision();
      const initial = api.provision.mock.calls[0][0].requestId;
      const changed = field === 'ruc' ? '1790000000002' : field === 'email' ? 'changed@test.local' : component.form.controls[field].value + '-changed';
      component.form.controls[field].setValue(changed); component.provision();
      expect(api.provision.mock.calls[1][0].requestId).not.toBe(initial);
    });
  it('success clears password, closes dialog, refreshes list and opens created tenant', () => {
    validForm(); api.provision.mockReturnValue(of({ tenant: detail, wasAlreadyProcessed: false })); component.provision();
    expect(component.form.controls.password.value).toBe(''); expect(component.provisionVisible).toBe(false);
    expect(component.detail()?.company.id).toBe(tenant.id); expect(component.notice()).toContain('creada');
    expect(api.tenants).toHaveBeenCalledTimes(2); expect(api.events).toHaveBeenCalledWith(tenant.id, 1, 10);
  });
  it('legacy detail remains usable with no lifecycle events', () => {
    component.openDetail(tenant); expect(component.detail()?.userCount).toBe(1); expect(component.events()).toEqual([]);
    expect(component.detailVisible).toBe(true); component.loadEvents({ first: 20, rows: 10 });
    expect(api.events).toHaveBeenLastCalledWith(tenant.id, 3, 10);
  });
  it('keeps the latest list filter/page when GETs return in reverse order', () => {
    const old = new Subject<PlatformPage<Tenant>>(); const current = new Subject<PlatformPage<Tenant>>();
    api.tenants.mockReturnValueOnce(old).mockReturnValueOnce(current);
    component.search = 'old'; component.filter();
    component.search = 'latest'; component.status = 'Suspended'; component.page({ first: 50, rows: 25 });
    expect(api.tenants).toHaveBeenLastCalledWith('latest', 'Suspended', 3, 25);
    current.next(tenantPage(otherTenant, 51)); current.complete();
    old.next(tenantPage(tenant, 42)); old.complete();
    expect(component.tenants()).toEqual([otherTenant]); expect(component.total()).toBe(51);
    expect(component.first).toBe(50); expect(component.rows).toBe(25); expect(component.loading()).toBe(false);
  });
  it('keeps the last selected detail and only starts its event GET in reverse order', () => {
    const old = new Subject<TenantDetail>(); const current = new Subject<TenantDetail>();
    api.detail.mockReturnValueOnce(old).mockReturnValueOnce(current);
    component.openDetail(tenant); component.openDetail(otherTenant);
    current.next(otherDetail); current.complete(); old.next(detail); old.complete();
    expect(component.detail()).toEqual(otherDetail); expect(component.detailLoading()).toBe(false);
    expect(api.events).toHaveBeenCalledTimes(1); expect(api.events).toHaveBeenCalledWith(otherTenant.id, 1, 10);
  });
  it('keeps the latest event page and total when GETs return in reverse order', () => {
    const old = new Subject<PlatformPage<TenantEvent>>(); const current = new Subject<PlatformPage<TenantEvent>>();
    api.events.mockReturnValueOnce(old).mockReturnValueOnce(current);
    component.openDetail(tenant); component.loadEvents({ first: 20, rows: 10 });
    expect(api.events).toHaveBeenLastCalledWith(tenant.id, 3, 10);
    current.next(eventPage(3, 21)); current.complete(); old.next(eventPage(1, 10)); old.complete();
    expect(component.events()).toEqual(eventPage(3).items); expect(component.eventTotal()).toBe(21);
    expect(component.eventFirst).toBe(20); expect(component.eventLoading()).toBe(false);
  });
  it('invalidates old-company events immediately while the next detail is still pending', () => {
    const old = new Subject<PlatformPage<TenantEvent>>(); const current = new Subject<TenantDetail>();
    api.events.mockReturnValueOnce(old); component.openDetail(tenant);
    old.next(eventPage(1, 30));
    api.detail.mockReturnValueOnce(current); component.openDetail(otherTenant);
    expect(component.detail()).toBeNull(); expect(component.events()).toEqual([]); expect(component.eventTotal()).toBe(0);
    expect(component.eventFirst).toBe(0); expect(component.eventLoading()).toBe(false);
    old.next(eventPage(2, 40)); old.error(failure);
    expect(component.events()).toEqual([]); expect(component.eventTotal()).toBe(0);
    expect(component.error()).toBe(''); expect(component.detailLoading()).toBe(true);
    current.next(otherDetail); current.complete(); expect(component.detail()).toEqual(otherDetail);
    expect(api.events).toHaveBeenLastCalledWith(otherTenant.id, 1, 10);
  });
  it.each(['before', 'after'] as const)('reports the current list error %s closing an independently opened detail', timing => {
    const list = new Subject<PlatformPage<Tenant>>(); const pendingDetail = new Subject<TenantDetail>();
    api.tenants.mockReturnValueOnce(list); component.load();
    api.detail.mockReturnValueOnce(pendingDetail); component.openDetail(otherTenant);
    if (timing === 'after') component.closeDetail();
    list.error(failure); expect(component.error()).toContain('RUC'); expect(component.loading()).toBe(false);
    if (timing === 'before') component.closeDetail();
    pendingDetail.error(new Error('synthetic-stale-detail'));
    expect(component.error()).toContain('RUC'); expect(component.tenants()).toEqual([tenant]);
    expect(component.detailVisible).toBe(false); expect(component.detailLoading()).toBe(false);
  });
  it('reports a current detail error after an independent list refresh starts', () => {
    const pendingDetail = new Subject<TenantDetail>(); const list = new Subject<PlatformPage<Tenant>>();
    api.detail.mockReturnValueOnce(pendingDetail); component.openDetail(tenant);
    api.tenants.mockReturnValueOnce(list); component.load(); pendingDetail.error(failure);
    expect(component.error()).toContain('RUC'); expect(component.detailLoading()).toBe(false); expect(component.loading()).toBe(true);
    list.next(tenantPage(otherTenant)); list.complete();
    expect(component.error()).toContain('RUC'); expect(component.tenants()).toEqual([otherTenant]);
  });
  it.each(['list', 'detail'] as const)('does not let an independent %s read error suppress the other current read error', first => {
    const list = new Subject<PlatformPage<Tenant>>(); const pendingDetail = new Subject<TenantDetail>();
    api.tenants.mockReturnValueOnce(list); component.load(); api.detail.mockReturnValueOnce(pendingDetail); component.openDetail(tenant);
    const initial = first === 'list' ? list : pendingDetail; const last = first === 'list' ? pendingDetail : list;
    initial.error(failure); expect(component.error()).toContain('RUC'); expect(readLoading(first)).toBe(false);
    expect(readLoading(first === 'list' ? 'detail' : 'list')).toBe(true);
    last.error(new HttpErrorResponse({ status: 400, error: { error: 'TENANT_REASON_REQUIRED' } }));
    expect(component.error()).toContain('motivo'); expect(component.loading()).toBe(false); expect(component.detailLoading()).toBe(false);
  });
  it.each(['open', 'closed'] as const)('reports the post-provision list refresh error with the created detail %s', visibility => {
    validForm(); const list = new Subject<PlatformPage<Tenant>>(); const pendingDetail = new Subject<TenantDetail>();
    api.tenants.mockReturnValueOnce(list); api.detail.mockReturnValueOnce(pendingDetail);
    api.provision.mockReturnValue(of({ tenant: otherDetail, wasAlreadyProcessed: false })); component.provision();
    expect(api.tenants).toHaveBeenLastCalledWith('', null, 1, 20); expect(api.detail).toHaveBeenLastCalledWith(otherTenant.id);
    expect(component.notice()).toContain('creada'); expect(component.saving()).toBe(false); expect(component.provisionVisible).toBe(false);
    if (visibility === 'closed') component.closeDetail();
    list.error(failure); expect(component.error()).toContain('RUC'); expect(component.loading()).toBe(false);
    pendingDetail.next(otherDetail); pendingDetail.complete();
    expect(component.error()).toContain('RUC'); expect(component.detail()).toEqual(visibility === 'closed' ? null : otherDetail);
    expect(api.events).toHaveBeenCalledTimes(visibility === 'closed' ? 0 : 1);
    expect(component.form.controls.password.value).toBe('');
    expect(api.provision.mock.calls[0][0].requestId).toMatch(/^[0-9a-f-]{36}$/);
  });
  describe.each<ReadScope>(['list', 'detail', 'events'])('%s read state', scope => {
    it.each(['complete', 'error'] as const)('ignores stale %s/finalize while the latest read is pending', terminal => {
      const old = new Subject<ReadResult>(); const current = new Subject<ReadResult>();
      startRead(scope, old); startRead(scope, current);
      if (terminal === 'error') old.error(failure); else old.complete();
      expect(readLoading(scope)).toBe(true); expect(component.error()).toBe('');
      current.error(failure); expect(readLoading(scope)).toBe(false); expect(component.error()).toContain('RUC');
    });
    it('preserves the latest error when an obsolete GET errors later', () => {
      const old = new Subject<ReadResult>(); const current = new Subject<ReadResult>();
      startRead(scope, old); startRead(scope, current); current.error(new Error('synthetic-only'));
      const currentError = component.error(); expect(currentError).not.toBe('');
      old.error(failure); expect(component.error()).toBe(currentError); expect(readLoading(scope)).toBe(false);
    });
    it.each(['complete', 'error'] as const)('ignores data and %s after destruction and cannot restart GETs', terminal => {
      const pending = new Subject<ReadResult>(); startRead(scope, pending); component.ngOnDestroy();
      const calls = [api.tenants.mock.calls.length, api.detail.mock.calls.length, api.events.mock.calls.length];
      const before = { list: component.tenants(), total: component.total(), detail: component.detail(),
        events: component.events(), eventTotal: component.eventTotal(), error: component.error() };
      pending.next(scope === 'list' ? tenantPage(otherTenant) : scope === 'detail' ? otherDetail : eventPage(2));
      if (terminal === 'error') pending.error(failure); else pending.complete();
      component.load(); component.openDetail(tenant); component.loadEvents();
      expect([api.tenants.mock.calls.length, api.detail.mock.calls.length, api.events.mock.calls.length]).toEqual(calls);
      expect({ list: component.tenants(), total: component.total(), detail: component.detail(),
        events: component.events(), eventTotal: component.eventTotal(), error: component.error() }).toEqual(before);
      expect(readLoading(scope)).toBe(false); expect(component.detailVisible).toBe(false);
    });
  });
  describe.each<ReadScope>(['detail', 'events'])('closing %s', scope => {
    it.each(['complete', 'error'] as const)('invalidates pending data and %s without affecting the list', terminal => {
      const pending = new Subject<ReadResult>(); const list = new Subject<PlatformPage<Tenant>>();
      api.tenants.mockReturnValueOnce(list); component.load(); startRead(scope, pending); component.closeDetail();
      pending.next(scope === 'detail' ? detail : eventPage(1));
      if (terminal === 'error') pending.error(failure); else pending.complete();
      expect(component.detailVisible).toBe(false); expect(component.detail()).toBeNull(); expect(component.events()).toEqual([]);
      expect(component.eventTotal()).toBe(0); expect(component.detailLoading()).toBe(false); expect(component.eventLoading()).toBe(false);
      expect(component.error()).toBe(''); expect(component.loading()).toBe(true);
      list.next(tenantPage(otherTenant)); list.complete(); expect(component.tenants()).toEqual([otherTenant]);
    });
    it('keeps a reopened same-company read pending when the previous read errors', () => {
      const old = new Subject<ReadResult>(); const current = new Subject<ReadResult>();
      startRead(scope, old); component.closeDetail(); startRead(scope, current); old.error(failure);
      expect(readLoading(scope)).toBe(true); expect(component.error()).toBe(''); expect(component.detailVisible).toBe(true);
      current.next(scope === 'detail' ? detail : eventPage(2)); current.complete(); expect(readLoading(scope)).toBe(false);
    });
  });
  describe.each<ReadScope>(['list', 'detail', 'events'])('%s shared mutation error', scope => {
    it.each(['provision', 'lifecycle'] as const)('preserves the current %s error against GET errors and stale finalize', mutation => {
      if (mutation === 'provision') validForm();
      else { component.openLifecycle(tenant, false); component.reason = 'Synthetic suspension'; }
      const old = new Subject<ReadResult>(); const current = new Subject<ReadResult>();
      startRead(scope, old); startRead(scope, current);
      const mutationFailure = new HttpErrorResponse({ status: 400, error: { error: 'TENANT_REASON_REQUIRED' } });
      if (mutation === 'provision') { api.provision.mockReturnValue(throwError(() => mutationFailure)); component.provision(); }
      else { api.setActive.mockReturnValue(throwError(() => mutationFailure)); component.lifecycle(); }
      const mutationError = component.error(); expect(mutationError).toContain('motivo'); expect(component.saving()).toBe(false);
      old.error(failure); expect(component.error()).toBe(mutationError); expect(readLoading(scope)).toBe(true);
      current.error(failure); expect(component.error()).toBe(mutationError); expect(readLoading(scope)).toBe(false);
    });
    it('preserves a newer local dialog validation error against a pending GET', () => {
      const pending = new Subject<ReadResult>(); startRead(scope, pending);
      component.openLifecycle(tenant, false); component.lifecycle();
      const dialogError = component.error(); expect(dialogError).toContain('motivo');
      pending.error(failure); expect(component.error()).toBe(dialogError); expect(readLoading(scope)).toBe(false);
      expect(api.setActive).not.toHaveBeenCalled();
    });
  });
  it.each(['provision', 'lifecycle'] as const)('detail success and its child event GET preserve a newer %s error', mutation => {
    if (mutation === 'provision') validForm();
    else { component.openLifecycle(tenant, false); component.reason = 'Synthetic suspension'; }
    const pending = new Subject<TenantDetail>(); const events = new Subject<PlatformPage<TenantEvent>>();
    api.detail.mockReturnValueOnce(pending); api.events.mockReturnValueOnce(events); component.openDetail(tenant);
    if (mutation === 'provision') { api.provision.mockReturnValue(throwError(() => failure)); component.provision(); }
    else { api.setActive.mockReturnValue(throwError(() => failure)); component.lifecycle(); }
    const mutationError = component.error(); expect(mutationError).toContain('RUC');
    pending.next(detail); pending.complete(); expect(component.detail()).toEqual(detail);
    expect(component.error()).toBe(mutationError); expect(component.eventLoading()).toBe(true);
    events.error(new Error('synthetic-only')); expect(component.error()).toBe(mutationError); expect(component.eventLoading()).toBe(false);
  });
  it('suspension requires explicit reason, blocks double click and refreshes detail/events', () => {
    component.openDetail(tenant); component.openLifecycle(tenant, false); component.lifecycle(); expect(api.setActive).not.toHaveBeenCalled();
    component.reason = '  Synthetic suspension  '; const pending = new Subject<TenantDetail>(); api.setActive.mockReturnValue(pending);
    component.lifecycle(); component.lifecycle(); expect(api.setActive).toHaveBeenCalledTimes(1);
    expect(api.setActive).toHaveBeenCalledWith(tenant.id, false, 'Synthetic suspension');
    pending.next({ ...detail, company: { ...tenant, isActive: false } }); pending.complete();
    expect(component.lifecycleTarget).toBeNull(); expect(component.notice()).toContain('suspendida');
    expect(component.detail()?.company.isActive).toBe(false); expect(api.events).toHaveBeenCalledTimes(2);
  });
  it('reactivation uses a separate action and shows normalized errors without closing confirmation', () => {
    component.openLifecycle({ ...tenant, isActive: false }, true); component.reason = 'Synthetic reactivation';
    api.setActive.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { error: 'TENANT_REASON_REQUIRED' } })));
    component.lifecycle(); expect(api.setActive).toHaveBeenCalledWith(tenant.id, true, 'Synthetic reactivation');
    expect(component.lifecycleTarget).not.toBeNull(); expect(component.error()).toContain('motivo');
    api.setActive.mockReturnValue(of(detail)); component.lifecycle(); expect(component.notice()).toContain('reactivada');
  });
  it.each([false, true])('lifecycle active=%s supersedes a pending selected-company detail GET and stale events', active => {
    const oldDetail = new Subject<TenantDetail>(); const oldEvents = new Subject<PlatformPage<TenantEvent>>();
    api.events.mockReturnValueOnce(oldEvents); component.openDetail(tenant);
    api.detail.mockReturnValueOnce(oldDetail); component.openDetail(tenant);
    const result = { ...detail, company: { ...tenant, isActive: active } };
    component.openLifecycle(tenant, active); component.reason = 'Synthetic lifecycle'; api.setActive.mockReturnValue(of(result));
    component.lifecycle(); expect(api.setActive).toHaveBeenCalledWith(tenant.id, active, 'Synthetic lifecycle');
    expect(component.detail()).toEqual(result); expect(component.detailLoading()).toBe(false);
    oldDetail.next({ ...detail, company: { ...tenant, isActive: !active } }); oldDetail.complete(); oldEvents.error(failure);
    expect(component.detail()).toEqual(result); expect(component.error()).toBe('');
    expect(api.events).toHaveBeenCalledTimes(2); expect(api.events).toHaveBeenLastCalledWith(tenant.id, 1, 10);
  });
  it('lifecycle result refreshes events without allowing an older event page to overwrite them', () => {
    const old = new Subject<PlatformPage<TenantEvent>>(); const current = new Subject<PlatformPage<TenantEvent>>();
    api.events.mockReturnValueOnce(old).mockReturnValueOnce(current); component.openDetail(tenant);
    component.openLifecycle(tenant, false); component.reason = 'Synthetic suspension';
    api.setActive.mockReturnValue(of({ ...detail, company: { ...tenant, isActive: false } })); component.lifecycle();
    old.error(failure); expect(component.eventLoading()).toBe(true); expect(component.error()).toBe('');
    current.next(eventPage(2)); current.complete(); expect(component.events()).toEqual(eventPage(2).items);
  });
  it.each(['other company', 'closed'] as const)('lifecycle result does not replace detail selected as %s', selection => {
    const pending = new Subject<TenantDetail>(); component.openDetail(tenant); component.openLifecycle(tenant, false);
    component.reason = 'Synthetic suspension'; api.setActive.mockReturnValue(pending); component.lifecycle();
    if (selection === 'closed') component.closeDetail(); else { api.detail.mockReturnValueOnce(of(otherDetail)); component.openDetail(otherTenant); }
    const eventCalls = api.events.mock.calls.length;
    pending.next({ ...detail, company: { ...tenant, isActive: false } }); pending.complete();
    expect(component.detail()).toEqual(selection === 'closed' ? null : otherDetail);
    expect(api.events).toHaveBeenCalledTimes(eventCalls); expect(component.saving()).toBe(false);
    expect(component.notice()).toContain('suspendida');
  });
  it('provision success supersedes pending list/detail reads without changing RequestId behavior', () => {
    const oldList = new Subject<PlatformPage<Tenant>>(); const oldDetail = new Subject<TenantDetail>();
    api.tenants.mockReturnValueOnce(oldList); component.load(); api.detail.mockReturnValueOnce(oldDetail); component.openDetail(tenant);
    validForm(); api.tenants.mockReturnValue(of(tenantPage(otherTenant))); api.detail.mockReturnValue(of(otherDetail));
    api.provision.mockReturnValue(of({ tenant: otherDetail, wasAlreadyProcessed: false })); component.provision();
    oldList.next(tenantPage(tenant)); oldList.complete(); oldDetail.error(failure);
    expect(component.tenants()).toEqual([otherTenant]); expect(component.detail()).toEqual(otherDetail); expect(component.error()).toBe('');
    expect(component.form.controls.password.value).toBe(''); expect(component.saving()).toBe(false);
    expect(api.provision.mock.calls[0][0].requestId).toMatch(/^[0-9a-f-]{36}$/);
    expect(api.events).toHaveBeenCalledTimes(1); expect(api.events).toHaveBeenCalledWith(otherTenant.id, 1, 10);
  });
  it('dialog visibleChange invalidates a detail GET immediately on close', () => {
    const pending = new Subject<TenantDetail>(); api.detail.mockReturnValueOnce(pending);
    const fixture = TestBed.createComponent(PlatformTenants); fixture.componentInstance.openDetail(tenant); fixture.detectChanges();
    const dialog = fixture.debugElement.queryAll(By.directive(Dialog)).find(element => element.componentInstance.header === 'Detalle de empresa');
    expect(dialog).toBeDefined(); dialog!.componentInstance.visibleChange.emit(false);
    pending.next(detail); pending.complete();
    expect(fixture.componentInstance.detailVisible).toBe(false); expect(fixture.componentInstance.detail()).toBeNull();
    expect(api.events).not.toHaveBeenCalled(); fixture.destroy();
  });
  it('renders directory and provision form without POS navigation or operational data', () => {
    const fixture = TestBed.createComponent(PlatformTenants);
    fixture.componentInstance.openProvision(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tenants'); expect(fixture.nativeElement.textContent).toContain('Synthetic Tenant');
    expect(document.querySelector('input[formControlName="password"]')?.getAttribute('type')).toBe('password');
    expect(fixture.nativeElement.textContent).not.toContain('Ventas recientes');
    fixture.destroy();
  });
});
