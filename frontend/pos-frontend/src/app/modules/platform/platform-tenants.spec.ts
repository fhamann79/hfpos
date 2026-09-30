import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PlatformTenants } from './platform-tenants';
import { PlatformApi } from './platform-api.service';
import { PlatformStore } from './platform.store';
import { ProvisionResult, Tenant, TenantDetail } from './platform.model';

const tenant: Tenant = { id: 1, name: 'Synthetic Tenant', ruc: '1790000000001', timeZoneId: 'America/Guayaquil', isActive: true, createdAt: '2026-09-30T12:00:00Z' };
const detail: TenantDetail = { company: tenant, establishmentCount: 1, activeEstablishmentCount: 1, emissionPointCount: 1,
  activeEmissionPointCount: 1, userCount: 1, activeUserCount: 1, events: [] };
const emptyEvents = { items: [], totalItems: 0, page: 1, pageSize: 10, totalPages: 0 };
const failure = new HttpErrorResponse({ status: 409, error: { error: 'TENANT_RUC_ALREADY_EXISTS' } });

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
  it('renders directory and provision form without POS navigation or operational data', () => {
    const fixture = TestBed.createComponent(PlatformTenants);
    fixture.componentInstance.openProvision(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Tenants'); expect(fixture.nativeElement.textContent).toContain('Synthetic Tenant');
    expect(document.querySelector('input[formControlName="password"]')?.getAttribute('type')).toBe('password');
    expect(fixture.nativeElement.textContent).not.toContain('Ventas recientes');
    fixture.destroy();
  });
});
