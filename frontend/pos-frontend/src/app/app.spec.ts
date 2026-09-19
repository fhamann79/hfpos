import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { MeResponse } from './core/models/me';
import { PermissionService } from './core/services/permission.service';
import { AuthStore } from './core/stores/auth.store';
import { App } from './app';

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
          { path: 'dashboard', component: DashboardRouteStub },
        ]),
        { provide: AuthStore, useValue: authStore },
        { provide: PermissionService, useValue: permissionService },
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
});
