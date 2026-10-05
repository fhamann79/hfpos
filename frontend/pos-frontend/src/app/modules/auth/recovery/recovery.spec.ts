import { Location } from '@angular/common';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError, Subject } from 'rxjs';
import { vi } from 'vitest';
import { routes } from '../../../app.routes';
import { PasswordRecoveryApi } from '../../../core/services/password-recovery.service';
import { AuthStore } from '../../../core/stores/auth.store';
import { authInterceptor } from '../../../core/interceptors/auth.interceptor';
import { PlatformStore } from '../../platform/platform.store';
import { PasswordPage } from './password-page';
import { RecoverAccess } from './recover-access';
import { RecoveryDialog } from './recovery-dialog';

const secret = `t.${'a'.repeat(32)}.${'B'.repeat(64)}`;
const password = 'synthetic password only';

describe('HF One public recovery routing and forms', () => {
  const complete = vi.fn(() => of(undefined)); const change = vi.fn(() => of(undefined));
  beforeEach(() => {
    complete.mockReset().mockReturnValue(of(undefined)); change.mockReset().mockReturnValue(of(undefined));
    TestBed.configureTestingModule({ providers: [provideNoopAnimations(), provideRouter(routes),
      { provide: PasswordRecoveryApi, useValue: { complete, change } },
      { provide: AuthStore, useValue: { clear: vi.fn() } }, { provide: PlatformStore, useValue: { clear: vi.fn() } },
    ] });
  });
  it('renders the actual assisted information route with branding and login link', async () => {
    const harness = await RouterTestingHarness.create(); await harness.navigateByUrl('/recover-access', RecoverAccess);
    expect(harness.routeNativeElement?.textContent).toContain('Contacta a un operador autorizado');
    expect(harness.routeNativeElement?.querySelector('img')?.getAttribute('src')).toBe('/hf-one-logo.svg');
    expect(harness.routeNativeElement?.querySelector('a')?.getAttribute('href')).toBe('/login');
  });
  it('uses only fragment memory, cleans browser URL immediately, consumes and clears form', async () => {
    const clean = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl(`/recover-access/complete#token=${secret}`, PasswordPage);
    expect(page.hasToken).toBe(true); expect(clean).toHaveBeenCalled();
    expect(localStorage.getItem('recovery_token')).toBeNull();
    page.form.patchValue({ newPassword: password, confirmation: password }); page.submit();
    expect(complete).toHaveBeenCalledWith(false, secret, password); expect(page.success()).toBe(true);
    expect(page.form.getRawValue()).toEqual({ currentPassword: '', newPassword: '', confirmation: '' });
    harness.detectChanges(); expect(harness.routeNativeElement?.textContent).toContain('Contrase\u00f1a actualizada');
  });
  it.each([11, 257])('rejects %s characters and rejects confirmation mismatch', async length => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl(`/recover-access/complete#token=${secret}`, PasswordPage);
    page.form.patchValue({ newPassword: 'a'.repeat(length), confirmation: 'a'.repeat(length) }); page.submit();
    expect(complete).not.toHaveBeenCalled();
    page.form.patchValue({ newPassword: password, confirmation: 'other password only' }); page.submit();
    expect(complete).not.toHaveBeenCalled(); expect(page.error()).toContain('no coinciden');
  });
  it.each([12, 256])('accepts %s plain lowercase characters', async length => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl(`/recover-access/complete#token=${secret}`, PasswordPage);
    page.form.patchValue({ newPassword: 'a'.repeat(length), confirmation: 'a'.repeat(length) }); page.submit();
    expect(complete).toHaveBeenCalledOnce();
  });
  it('rejects query tokens and gives a brief generic failure for unavailable links', async () => {
    const harness = await RouterTestingHarness.create();
    const missing = await harness.navigateByUrl(`/recover-access/complete?token=${secret}`, PasswordPage);
    expect(missing.hasToken).toBe(false); missing.submit(); expect(complete).not.toHaveBeenCalled();
    await harness.navigateByUrl('/recover-access', RecoverAccess);
    const page = await harness.navigateByUrl(`/recover-access/complete#token=${secret}`, PasswordPage);
    complete.mockReturnValue(throwError(() => ({ status: 400, error: { error: 'RECOVERY_INVALID' } })));
    page.form.patchValue({ newPassword: password, confirmation: password }); page.submit();
    expect(page.error()).toContain('Solicita uno nuevo'); expect(page.form.controls.newPassword.value).toBe('');
  });
  it('routes platform consumption to its own API without a tenant forgot link', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl(`/platform/recover-access/complete#token=${secret.replace(/^t/, 'p')}`, PasswordPage);
    page.form.patchValue({ newPassword: password, confirmation: password }); page.submit();
    expect(complete).toHaveBeenCalledWith(true, secret.replace(/^t/, 'p'), password);
    harness.detectChanges(); expect(harness.routeNativeElement?.querySelector('a')?.getAttribute('href')).toBe('/platform/login');
  });
});

describe('Assisted operator dialog', () => {
  const issue = vi.fn(); const status = vi.fn(); const revoke = vi.fn();
  beforeEach(() => {
    issue.mockReset().mockReturnValue(of({ id: 'challenge', token: secret, expiresAt: '2030-01-01T00:00:00Z' }));
    status.mockReset().mockReturnValue(of([])); revoke.mockReset().mockReturnValue(of(undefined));
    TestBed.configureTestingModule({ imports: [RecoveryDialog], providers: [provideNoopAnimations(),
      { provide: PasswordRecoveryApi, useValue: { issue, status, revoke } }] });
  });
  function fixture() {
    const f = TestBed.createComponent(RecoveryDialog); f.componentRef.setInput('userId', 7); f.componentRef.setInput('username', 'owner');
    f.componentRef.setInput('visible', true); f.detectChanges(); return f;
  }
  it('requires identity evidence and never offers a new password field; hides link on close', () => {
    const f = fixture(); const dialog = f.componentInstance; dialog.issue(); expect(issue).not.toHaveBeenCalled();
    dialog.form.patchValue({ reason: 'Synthetic procedure', deliveryReference: 'Accredited channel', identityConfirmed: true });
    dialog.issue(); expect(issue).toHaveBeenCalledWith(false, 7, expect.objectContaining({ identityConfirmed: true }));
    expect(dialog.link()).toContain('#token='); f.detectChanges();
    expect(f.nativeElement.querySelector('[formControlName="newPassword"]')).toBeNull();
    dialog.close(); expect(dialog.link()).toBe('');
  });
  it('discards a secret response arriving after destroy', () => {
    const pending = new Subject<{ id: string; token: string; expiresAt: string }>(); issue.mockReturnValue(pending);
    const f = fixture(); const dialog = f.componentInstance;
    dialog.form.patchValue({ reason: 'reason', deliveryReference: 'channel', identityConfirmed: true }); dialog.issue();
    f.destroy(); pending.next({ id: 'id', token: secret, expiresAt: '2030-01-01T00:00:00Z' }); expect(dialog.link()).toBe('');
  });
});

describe('Public recovery HTTP isolation', () => {
  beforeEach(() => { localStorage.clear(); TestBed.configureTestingModule({ providers: [provideRouter([]),
    provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()] }); });
  afterEach(() => { TestBed.inject(HttpTestingController).verify(); localStorage.clear(); });
  it.each(['/api/account/recovery/complete', '/api/platform/account/recovery/complete'])('does not attach JWT or redirect/clear either session on %s 401', path => {
    const tenant = TestBed.inject(AuthStore); const platform = TestBed.inject(PlatformStore);
    tenant.setToken('tenant-session'); platform.setToken('platform-session');
    const router = TestBed.inject(Router); const navigate = vi.spyOn(router, 'navigate');
    TestBed.inject(HttpClient).post(path, { token: secret, newPassword: password }).subscribe({ error: () => {} });
    const request = TestBed.inject(HttpTestingController).expectOne(path);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ error: 'RECOVERY_INVALID' }, { status: 401, statusText: 'Unauthorized' });
    expect(tenant.token()).toBe('tenant-session'); expect(platform.token()).toBe('platform-session'); expect(navigate).not.toHaveBeenCalled();
  });
});
