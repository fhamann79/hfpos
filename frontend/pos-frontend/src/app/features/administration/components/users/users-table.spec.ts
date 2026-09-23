import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { User } from '../../models/user.model';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { UsersTable } from './users-table';

const user: User = {
  id: 7, username: 'cashier', email: 'cashier@hfpos.test',
  roleId: 2, roleCode: 'CASHIER', roleName: 'Caja', companyId: 1,
  establishmentId: 3, emissionPointId: 4, isActive: true,
};

describe('UserService session revocation', () => {
  beforeEach(() => TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('posts to the tenant-scoped user endpoint without a payload', () => {
    TestBed.inject(UserService).revokeSessions(7).subscribe();
    const request = TestBed.inject(HttpTestingController)
      .expectOne((req) => req.method === 'POST' && req.url.endsWith('/api/Users/7/revoke-sessions'));
    expect(request.request.body).toBeNull();
    request.flush(null);
  });
});

describe('UsersTable session revocation', () => {
  const confirm = vi.fn();
  const addMessage = vi.fn();
  const revokeSessions = vi.fn<(...args: [number]) => Observable<void>>();

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UsersTable],
      providers: [
        providePrimeNG({ unstyled: true }),
        { provide: UserService, useValue: { getAll: () => of([user]), revokeSessions } },
        { provide: RoleService, useValue: { getAll: () => of([]) } },
        { provide: ConfirmationService, useValue: { confirm } },
        { provide: MessageService, useValue: { add: addMessage } },
      ],
    });
  });

  afterEach(() => vi.clearAllMocks());

  it('shows the action only to writers and does not call the API before acceptance', async () => {
    const fixture = TestBed.createComponent(UsersTable);
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Cerrar sesiones');
    fixture.componentInstance.confirmRevokeSessions(user);
    expect(confirm).not.toHaveBeenCalled();

    fixture.componentRef.setInput('canWrite', true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cerrar sesiones');
    fixture.componentInstance.confirmRevokeSessions(user);
    expect(confirm).toHaveBeenCalledOnce();
    expect(confirm.mock.calls[0][0].message).toContain('no desactiva ni elimina');
    expect(revokeSessions).not.toHaveBeenCalled();
  });

  it('accepts once, blocks duplicate invocation, and reports success', () => {
    const pending = new Subject<void>();
    revokeSessions.mockReturnValue(pending.asObservable());
    const component = TestBed.runInInjectionContext(() => new UsersTable());
    component.canWrite = true;
    component.confirmRevokeSessions(user);
    const accept = confirm.mock.calls[0][0].accept as () => void;
    accept();
    accept();
    component.confirmRevokeSessions(user);
    expect(revokeSessions).toHaveBeenCalledOnce();
    expect(component.revokingUserId()).toBe(user.id);
    pending.next();
    pending.complete();
    expect(component.revokingUserId()).toBeNull();
    expect(addMessage).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }));
  });

  it('normalizes an API error and releases the pending state', () => {
    revokeSessions.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { error: 'LAST_ACTIVE_ADMIN_REQUIRED' },
    })));
    const component = TestBed.runInInjectionContext(() => new UsersTable());
    component.canWrite = true;
    component.confirmRevokeSessions(user);
    (confirm.mock.calls[0][0].accept as () => void)();
    expect(component.revokingUserId()).toBeNull();
    expect(addMessage).toHaveBeenCalledWith(expect.objectContaining({
      severity: 'error',
      detail: 'Debe permanecer al menos un administrador activo en la empresa.',
    }));
  });
});
