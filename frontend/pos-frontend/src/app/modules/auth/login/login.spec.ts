import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { vi } from 'vitest';
import { MeResponse } from '../../../core/models/me';
import { AuthService } from '../../../core/services/auth';
import { AuthStore } from '../../../core/stores/auth.store';
import { Login } from './login';

describe('Login', () => {
  let component: Login;
  let fixture: ComponentFixture<Login>;
  let queryParams: BehaviorSubject<ParamMap>;
  let authService: Pick<AuthService, 'login'>;
  let authStore: Pick<AuthStore, 'setToken' | 'loadMe'>;
  let router: Pick<Router, 'navigate'>;

  const meResponse: MeResponse = {
    userId: '1',
    username: 'test-user',
    companyId: 1,
    companyTimeZoneId: 'America/Guayaquil',
    establishmentId: 1,
    emissionPointId: 1,
    roleCode: 'CASHIER',
    permissions: [],
  };

  beforeEach(async () => {
    queryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    authService = {
      login: vi.fn(() => of({ token: 'unit-test-token' })),
    };
    authStore = {
      setToken: vi.fn(),
      loadMe: vi.fn(() => of(meResponse)),
    };
    router = {
      navigate: vi.fn(() => Promise.resolve(true)),
    };

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideNoopAnimations(),
        { provide: ActivatedRoute, useValue: { queryParamMap: queryParams.asObservable() } },
        { provide: AuthService, useValue: authService },
        { provide: AuthStore, useValue: authStore },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => {
    queryParams.complete();
    vi.restoreAllMocks();
  });

  it('creates with explicit route and authentication dependencies', () => {
    expect(component).toBeTruthy();
  });

  it('shows the session expired message from query parameters', () => {
    queryParams.next(convertToParamMap({ message: 'session-expired' }));
    fixture.detectChanges();

    expect(component.errorMessage()).toBe('Tu sesión expiró. Inicia sesión nuevamente.');
  });

  it('does not call login when the form is invalid', () => {
    component.submitLogin();

    expect(authService.login).not.toHaveBeenCalled();
    expect(component.loginForm.controls.username.touched).toBe(true);
    expect(component.loginForm.controls.password.touched).toBe(true);
  });

  it('stores the token, loads the user and navigates after a successful login', () => {
    component.loginForm.setValue({ username: ' cashier ', password: 'secret' });

    component.submitLogin();

    expect(authService.login).toHaveBeenCalledWith('cashier', 'secret');
    expect(authStore.setToken).toHaveBeenCalledWith('unit-test-token');
    expect(authStore.loadMe).toHaveBeenCalledOnce();
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    expect(component.loading()).toBe(false);
  });
});
