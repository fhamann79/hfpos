import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { of } from 'rxjs';
import { MeResponse } from './core/models/me';
import { PermissionService } from './core/services/permission.service';
import { AuthStore } from './core/stores/auth.store';
import { App } from './app';
import { PasswordRecoveryApi } from './core/services/password-recovery.service';
import { PlatformStore } from './modules/platform/platform.store';
import { RecoverAccess } from './modules/auth/recovery/recover-access';
import { PasswordPage } from './modules/auth/recovery/password-page';

@Component({
  standalone: true,
  template: '<p data-testid="login-route">Login route</p>',
})
class LoginRouteStub {}

@Component({
  standalone: true,
  template: '<p data-testid="dashboard-route">Dashboard route</p>',
})
class DashboardRouteStub {}

describe('App', () => {
  let router: Router;

  const authenticatedUser: MeResponse = {
    userId: '1',
    username: 'test-user',
    companyId: 1,
    companyTimeZoneId: 'America/Guayaquil',
    establishmentId: 1,
    emissionPointId: 1,
    roleCode: 'ADMIN',
    permissions: [],
  };

  const authStore = {
    me: signal<MeResponse | null>(authenticatedUser),
    clear: vi.fn(),
  } satisfies Pick<AuthStore, 'me' | 'clear'>;

  const permissionService = {
    canAccess: vi.fn(() => false),
  } satisfies Pick<PermissionService, 'canAccess'>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: 'login', component: LoginRouteStub },
          { path: 'platform/login', component: LoginRouteStub },
          { path: 'platform/tenants', component: DashboardRouteStub },
          { path: 'dashboard', component: DashboardRouteStub },
          { path: 'recover-access', component: RecoverAccess },
          { path: 'recover-access/complete', component: PasswordPage },
          { path: 'platform/recover-access/complete', component: PasswordPage, data: { platform: true } },
          { path: 'account/password', component: PasswordPage, data: { self: true } },
        ]),
        { provide: AuthStore, useValue: authStore },
        { provide: PermissionService, useValue: permissionService },
        { provide: PasswordRecoveryApi, useValue: { complete: vi.fn(() => of(undefined)), change: vi.fn(() => of(undefined)) } },
        { provide: PlatformStore, useValue: { clear: vi.fn() } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('creates the application root', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the login route without the application shell', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await router.navigateByUrl('/login');
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-shell')).toBeNull();
    expect(compiled.querySelector('[data-testid="login-route"]')?.textContent).toContain(
      'Login route',
    );
  });

  it('renders protected routes inside the application shell', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await router.navigateByUrl('/dashboard');
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-shell')).not.toBeNull();
    expect(compiled.querySelector('[data-testid="dashboard-route"]')?.textContent).toContain(
      'Dashboard route',
    );
  });

  it.each(['/platform/login', '/platform/tenants'])('keeps platform route %s outside the tenant shell', async url => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await router.navigateByUrl(url);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-shell')).toBeNull();
    expect(fixture.componentInstance.showShell()).toBe(false);
  });

  it.each(['/recover-access', '/recover-access/complete', '/platform/recover-access/complete'])
    ('renders actual public access page %s without private navigation', async url => {
      authStore.me.set(null);
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      await router.navigateByUrl(url);
      await fixture.whenStable();
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(page.querySelector('app-shell')).toBeNull();
      expect(page.querySelectorAll('img[alt="HF One"]')).toHaveLength(1);
      expect(page.textContent).not.toContain('Dashboard');
      expect(page.textContent).not.toContain('Salir');
      authStore.me.set(authenticatedUser);
    });

  it('keeps the actual authenticated password form in the tenant shell', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await router.navigateByUrl('/account/password');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-shell')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#current-password')).not.toBeNull();
  });
});
