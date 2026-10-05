import { Location } from '@angular/common';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { App } from './app';
import { appConfig } from './app.config';
import { AuthGuard } from './core/guards/auth.guard';
import { AuthStore } from './core/stores/auth.store';

const secret = `t.${'a'.repeat(32)}.${'B'.repeat(64)}`;
// Frontend treats JWT as opaque; /me is the authority for this expired synthetic session.
const expiredJwt = `${btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))}.${btoa(JSON.stringify({ sub: '530', exp: 1 }))}.synthetic-test-signature`;

describe('Real application auth initializer and recovery navigation', () => {
  let previousUrl: string;
  beforeEach(() => {
    previousUrl = location.pathname + location.search + location.hash;
    localStorage.clear();
    localStorage.setItem('token', expiredJwt);
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
    history.replaceState(null, '', previousUrl);
  });

  function configure(url: string) {
    history.replaceState(null, '', url);
    TestBed.configureTestingModule({ imports: [App], providers: [
      ...appConfig.providers, provideHttpClientTesting(), provideNoopAnimations(),
    ] });
    // TestBed executes the actual appConfig initializer when constructing its injector.
    return TestBed.inject(HttpTestingController);
  }

  it.each(['/login', '/login/', '/recover-access', '/recover-access/',
    `/recover-access/complete#token=${secret}`, `/recover-access/complete/#token=${secret}`])
    ('never bootstraps a stored expired tenant JWT on public route %s', async url => {
      const requests = configure(url);
      requests.expectNone(req => req.url.endsWith('/api/auth/me'));
      await TestBed.inject(ApplicationInitStatus).donePromise;
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      const router = TestBed.inject(Router);
      await router.navigateByUrl(url);
      await fixture.whenStable();
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(page.querySelector('app-shell')).toBeNull();
      expect(TestBed.inject(AuthStore).loaded()).toBe(true);
      expect(TestBed.inject(AuthStore).token()).toBe(expiredJwt);
      if (url.replace(/\/+$/, '') === '/recover-access') {
        expect(page.textContent).toContain('Contacta a un operador autorizado');
      }
      if (url.startsWith('/recover-access/complete')) {
        expect(page.querySelector('#new-password')).not.toBeNull();
        expect(page.textContent).not.toContain('El enlace no es');
        expect(TestBed.inject(Location).path(true)).toBe('/recover-access/complete');
        const password = 'new synthetic password';
        for (const id of ['new-password', 'confirm-password']) {
          const input = page.querySelector(`#${id}`) as HTMLInputElement;
          input.value = password;
          input.dispatchEvent(new Event('input', { bubbles: true }));
        }
        page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        const complete = requests.expectOne(req => req.url.endsWith('/api/account/recovery/complete'));
        expect(complete.request.headers.has('Authorization')).toBe(false);
        expect(complete.request.body).toEqual({ token: secret, newPassword: password });
        complete.flush(null);
        fixture.detectChanges();
        expect(page.textContent).toContain('Contrase\u00f1a actualizada');
      }
    });

  it.each(['/account/password', '/recover-access/complete/extra'])
    ('still bootstraps non-public access %s and rejects an expired JWT on actual /me 401', async url => {
    const requests = configure(url);
    const request = requests.expectOne(req => req.url.endsWith('/api/auth/me'));
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${expiredJwt}`);
    request.flush({ error: 'SESSION_STALE' }, { status: 401, statusText: 'Unauthorized' });
    await TestBed.inject(ApplicationInitStatus).donePromise;
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/account/password');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(TestBed.inject(AuthStore).token()).toBeNull();
    expect(TestBed.inject(AuthGuard).canActivate()).toBe(false);
    expect(router.url).toContain('/login');
    expect(fixture.nativeElement.querySelector('#current-password')).toBeNull();
    history.replaceState(null, '', `/recover-access/complete#token=${secret}`);
    await router.navigateByUrl(`/recover-access/complete#token=${secret}`);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#new-password')).not.toBeNull();
    expect(TestBed.inject(Location).path(true)).toBe('/recover-access/complete');
  });

  it('keeps authenticated private bootstrap and self-change reachable', async () => {
    const requests = configure('/account/password');
    requests.expectOne(req => req.url.endsWith('/api/auth/me')).flush({
      userId: '530', username: 'owner', companyId: 1, establishmentId: 1, emissionPointId: 1,
      companyTimeZoneId: 'America/Guayaquil', roleCode: 'CASHIER', permissions: [],
    });
    await TestBed.inject(ApplicationInitStatus).donePromise;
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/account/password');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(TestBed.inject(AuthGuard).canActivate()).toBe(true);
    expect(fixture.nativeElement.querySelector('app-shell')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#current-password')).not.toBeNull();
  });
});
