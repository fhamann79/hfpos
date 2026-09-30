import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, provideRouter } from '@angular/router';
import { Observable } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../core/stores/auth.store';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { environment } from '../../../environments/environment';
import { PlatformStore } from './platform.store';
import { PlatformApi } from './platform-api.service';
import { platformGuard } from './platform.guard';
import { PlatformLogin } from './platform-login';

const me = { id: 1, username: 'synthetic-platform', email: 'platform@test.local', role: 'PLATFORM_ADMIN' };

describe('Platform security boundary', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let tenant: AuthStore;
  let platform: PlatformStore;
  let router: Router;
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpClient); requests = TestBed.inject(HttpTestingController);
    tenant = TestBed.inject(AuthStore); platform = TestBed.inject(PlatformStore); router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    tenant.setToken('synthetic-tenant-token'); platform.setToken('synthetic-platform-token');
  });
  afterEach(() => { requests.verify(); localStorage.clear(); vi.restoreAllMocks(); });

  it('sends only the platform token to the platform API', () => {
    http.get('/api/platform/tenants', { headers: { Authorization: 'Bearer wrong-token' } }).subscribe();
    const req = requests.expectOne('/api/platform/tenants');
    expect(req.request.headers.getAll('Authorization')).toEqual(['Bearer synthetic-platform-token']); req.flush({});
  });
  it('sends only the tenant token to the tenant API', () => {
    http.get('/api/Users').subscribe();
    const req = requests.expectOne('/api/Users');
    expect(req.request.headers.get('Authorization')).toBe('Bearer synthetic-tenant-token'); req.flush({});
  });
  it('never falls back to a token from the other plane', () => {
    platform.clear();
    http.get('/api/platform/tenants', { headers: { Authorization: 'Bearer tenant-token' } }).subscribe();
    const req = requests.expectOne('/api/platform/tenants'); expect(req.request.headers.has('Authorization')).toBe(false); req.flush({});
    platform.setToken('synthetic-platform-token'); tenant.clear();
    http.get('/api/Users').subscribe();
    const other = requests.expectOne('/api/Users'); expect(other.request.headers.has('Authorization')).toBe(false); other.flush({});
  });
  it.each(['https://external.invalid/api/platform/tenants', '//external.invalid/api/Users', '/assets/file.json'])
    ('does not attach either token outside our API: %s', url => {
      http.get(url).subscribe(); const req = requests.expectOne(url);
      expect(req.request.headers.has('Authorization')).toBe(false); req.flush({});
    });
  it('clears only platform storage on a platform 401', () => {
    http.get('/api/platform/tenants').subscribe({ error: () => {} });
    requests.expectOne('/api/platform/tenants').flush({ error: 'PLATFORM_SESSION_STALE' }, { status: 401, statusText: 'Unauthorized' });
    expect(platform.token()).toBeNull(); expect(localStorage.getItem('platform_token')).toBeNull();
    expect(tenant.token()).toBe('synthetic-tenant-token');
    expect(router.navigate).toHaveBeenCalledWith(['/platform/login']);
  });
  it('clears only tenant storage on a tenant 401 and preserves its redirect', () => {
    http.get('/api/Users').subscribe({ error: () => {} });
    requests.expectOne('/api/Users').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(tenant.token()).toBeNull(); expect(platform.token()).toBe('synthetic-platform-token');
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { message: 'session-expired' } });
  });
  it('invalid login does not clear an existing session in either plane', () => {
    TestBed.inject(PlatformApi).login('absent', 'synthetic-wrong').subscribe({ error: () => {} });
    requests.expectOne(`${environment.apiUrl}/api/platform/auth/login`).flush({ error: 'INVALID_CREDENTIALS' }, { status: 401, statusText: 'Unauthorized' });
    expect(tenant.token()).toBeTruthy(); expect(platform.token()).toBeTruthy(); expect(router.navigate).not.toHaveBeenCalled();
  });
  it('guard redirects without a platform token even if tenant login exists', () => {
    platform.clear();
    const result = TestBed.runInInjectionContext(() => platformGuard(new ActivatedRouteSnapshot(), { url: '/platform/tenants' } as RouterStateSnapshot));
    expect(router.serializeUrl(result as ReturnType<Router['createUrlTree']>)).toBe('/platform/login');
  });
  it('guard restores only platform context and permits PLATFORM_ADMIN', () => {
    const result = TestBed.runInInjectionContext(() => platformGuard(new ActivatedRouteSnapshot(), { url: '/platform/tenants' } as RouterStateSnapshot));
    let permitted: unknown;
    (result as Observable<unknown>).subscribe(value => permitted = value);
    requests.expectOne(`${environment.apiUrl}/api/platform/auth/me`).flush(me);
    expect(permitted).toBe(true); expect(platform.me()?.role).toBe('PLATFORM_ADMIN');
    expect(tenant.me()).toBeNull();
  });
  it('guard fails closed for a non-platform role', () => {
    const result = TestBed.runInInjectionContext(() => platformGuard(new ActivatedRouteSnapshot(), { url: '/platform/tenants' } as RouterStateSnapshot));
    let permitted: unknown;
    (result as Observable<unknown>).subscribe(value => permitted = value);
    requests.expectOne(`${environment.apiUrl}/api/platform/auth/me`).flush({ ...me, role: 'ADMIN' });
    expect(permitted).not.toBe(true); expect(platform.authenticated()).toBe(false);
  });
  it('platform login stores its token, loads me, clears password and navigates', () => {
    const component = TestBed.runInInjectionContext(() => new PlatformLogin());
    component.form.setValue({ username: ' synthetic-platform ', password: 'synthetic-password-only' });
    component.submit(); component.submit();
    const req = requests.expectOne(`${environment.apiUrl}/api/platform/auth/login`);
    expect(req.request.body).toEqual({ username: 'synthetic-platform', password: 'synthetic-password-only' });
    req.flush({ token: 'new-platform-token' });
    requests.expectOne(`${environment.apiUrl}/api/platform/auth/me`).flush(me);
    expect(platform.token()).toBe('new-platform-token'); expect(component.form.controls.password.value).toBe('');
    expect(router.navigate).toHaveBeenCalledWith(['/platform/tenants']); expect(component.loading()).toBe(false);
    expect(Object.values(localStorage)).not.toContain('synthetic-password-only');
  });
  it('platform login validates required input and shows a normalized invalid-credentials error', () => {
    const component = TestBed.runInInjectionContext(() => new PlatformLogin());
    component.submit(); expect(component.error()).toContain('Ingresa usuario');
    requests.expectNone(`${environment.apiUrl}/api/platform/auth/login`);
    component.form.setValue({ username: 'absent', password: 'synthetic-invalid' }); component.submit();
    requests.expectOne(`${environment.apiUrl}/api/platform/auth/login`).flush({ error: 'INVALID_CREDENTIALS' }, { status: 401, statusText: 'Unauthorized' });
    expect(component.error()).toBe('Credenciales inválidas.'); expect(component.loading()).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });
  it('API sends server-side search/status/page parameters without loading the whole directory', () => {
    TestBed.inject(PlatformApi).tenants('  synthetic  ', 'Suspended', 3, 25).subscribe();
    const req = requests.expectOne(r => r.url.endsWith('/api/platform/tenants'));
    expect(req.request.params.get('search')).toBe('synthetic'); expect(req.request.params.get('status')).toBe('Suspended');
    expect(req.request.params.get('page')).toBe('3'); expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ items: [], totalItems: 0, page: 3, pageSize: 25, totalPages: 0 });
  });
});
