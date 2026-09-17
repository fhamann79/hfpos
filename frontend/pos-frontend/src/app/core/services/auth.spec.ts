import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { AuthGuard } from '../guards/auth.guard';
import { administrationAccessGuard } from '../guards/administration-access.guard';
import { operationalStructureAccessGuard } from '../guards/operational-structure-access.guard';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { AuthStore } from '../stores/auth.store';
import { AuthService } from './auth';

describe('Tenant session and permission guards', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let store: AuthStore;
  let router: Router;

  function loadSession(permissions = ['ADMIN_USERS_READ', 'OP_STRUCTURE_READ']): void {
    store.setToken('unit-test-token');
    store.loadMe().subscribe();
    requests.expectOne((req) => req.url.endsWith('/api/auth/me')).flush({
      userId: '10', username: 'tenant-admin', companyId: 7, companyTimeZoneId: 'America/Guayaquil',
      establishmentId: 11, emissionPointId: 12, roleCode: 'ADMIN', permissions,
    });
  }

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([]),
    ] });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => { requests.verify(); localStorage.clear(); vi.restoreAllMocks(); });

  it('clears token and tenant context on 401 and requests a new login', () => {
    loadSession();
    http.get('/api/Users').subscribe({ error: () => {} });
    const request = requests.expectOne('/api/Users');
    expect(request.request.headers.get('Authorization')).toBe('Bearer unit-test-token');
    request.flush({ error: 'CONTEXT_MISMATCH' }, { status: 401, statusText: 'Unauthorized' });
    expect(store.token()).toBeNull();
    expect(store.me()).toBeNull();
    expect(TestBed.inject(AuthService).getContext()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { message: 'session-expired' } });
  });

  it('retains the authenticated session on 403', () => {
    loadSession();
    http.get('/api/Users').subscribe({ error: () => {} });
    requests.expectOne('/api/Users').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(store.isAuthenticated()).toBe(true);
    expect(TestBed.inject(AuthService).getContext()?.companyId).toBe(7);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not clear a valid cached session if a me refresh receives 403', () => {
    loadSession();
    store.loadMe().subscribe();
    requests.expectOne((req) => req.url.endsWith('/api/auth/me')).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(store.isAuthenticated()).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('preserves authorization guards and denies missing permissions', () => {
    expect(TestBed.inject(AuthGuard).canActivate()).toBe(false);
    loadSession();
    expect(TestBed.inject(AuthGuard).canActivate()).toBe(true);
    const route = new ActivatedRouteSnapshot();
    const state = { url: '/administration' } as RouterStateSnapshot;
    expect(TestBed.runInInjectionContext(() => administrationAccessGuard(route, state))).toBe(true);
    expect(TestBed.runInInjectionContext(() => operationalStructureAccessGuard(route, state))).toBe(true);
    loadSession([]);
    expect(TestBed.runInInjectionContext(() => administrationAccessGuard(route, state))).not.toBe(true);
    expect(TestBed.runInInjectionContext(() => operationalStructureAccessGuard(route, state))).not.toBe(true);
  });
});
