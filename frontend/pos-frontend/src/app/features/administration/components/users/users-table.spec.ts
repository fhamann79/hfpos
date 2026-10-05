import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { Table } from 'primeng/table';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PagedResult } from '../../../../core/models/paged-result.model';
import { User, UserPageQuery } from '../../models/user.model';
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

  it('requests default pagination without optional filters', () => {
    TestBed.inject(UserService).getPage().subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(req => req.url.endsWith('/api/Users'));
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('30');
    expect(request.request.params.keys()).toEqual(['page', 'pageSize']);
    request.flush({ items: [], page: 1, pageSize: 30, totalItems: 0, totalPages: 0 });
  });

  it('sends trimmed server search, inactive status and role on the requested page', () => {
    TestBed.inject(UserService).getPage({ page: 2, pageSize: 50, search: '  MAIL  ', isActive: false, roleId: 2 }).subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(req => req.url.endsWith('/api/Users'));
    expect(Object.fromEntries(request.request.params.keys().map(key => [key, request.request.params.get(key)])))
      .toEqual({ page: '2', pageSize: '50', search: 'MAIL', isActive: 'false', roleId: '2' });
    request.flush({ items: [user], page: 2, pageSize: 50, totalItems: 80, totalPages: 2 });
  });
});

describe('UsersTable server pagination', () => {
  const getPage = vi.fn<(query: UserPageQuery) => Observable<PagedResult<User>>>();
  const create = vi.fn(() => of(user));
  const update = vi.fn(() => of(undefined));
  const updatePassword = vi.fn(() => of(undefined));
  const deactivate = vi.fn(() => of(undefined));
  const revokeSessions = vi.fn(() => of(undefined));
  const confirm = vi.fn();

  const result = (page = 1, totalItems = 65): PagedResult<User> => ({
    items: [user], page, pageSize: 30, totalItems, totalPages: Math.ceil(totalItems / 30),
  });
  const component = () => TestBed.runInInjectionContext(() => new UsersTable());

  beforeEach(() => {
    vi.clearAllMocks();
    getPage.mockImplementation(query => of(result(query.page)));
    TestBed.configureTestingModule({
      imports: [UsersTable],
      providers: [
        providePrimeNG({ unstyled: true }),
        { provide: UserService, useValue: { getPage, create, update, updatePassword, delete: deactivate, revokeSessions } },
        { provide: RoleService, useValue: { getAll: () => of([]) } },
        { provide: ConfirmationService, useValue: { confirm } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
  });

  it('loads one initial server page and binds backend totalRecords to a lazy table', async () => {
    const fixture = TestBed.createComponent(UsersTable);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(getPage).toHaveBeenCalledOnce();
    expect(getPage).toHaveBeenCalledWith({ page: 1, pageSize: 30, search: '', isActive: null, roleId: null });
    const table = fixture.debugElement.query(By.directive(Table)).componentInstance as Table;
    expect(table.lazy).toBe(true);
    expect(table.totalRecords).toBe(65);
    expect(table.rows).toBe(30);
    expect(fixture.componentInstance.users()).toEqual([user]);
  });

  it('requests the next page through lazy events', () => {
    const table = component();
    table.onUsersLazyLoad({ first: 30, rows: 30 });
    expect(getPage).toHaveBeenCalledWith(expect.objectContaining({ page: 2, pageSize: 30 }));
    expect(table.first).toBe(30);
    expect(table.currentPage()).toBe(2);
  });

  it.each(['search', 'status', 'role'])('resets page one when the %s server filter changes', filter => {
    const table = component();
    table.loadUsers(2);
    if (filter === 'search') table.search = 'cashier';
    if (filter === 'status') table.isActive = false;
    if (filter === 'role') table.roleId = 2;
    table.applyFilters();
    expect(getPage).toHaveBeenLastCalledWith({ page: 1, pageSize: 30, search: table.search, isActive: table.isActive, roleId: table.roleId });
    expect(table.first).toBe(0);
  });

  it('clears all filters and resets the page', () => {
    const table = component();
    table.search = 'cashier'; table.isActive = false; table.roleId = 2;
    table.loadUsers(2);
    table.clearFilters();
    expect(getPage).toHaveBeenLastCalledWith({ page: 1, pageSize: 30, search: '', isActive: null, roleId: null });
  });

  it('ignores stale successes and errors after a newer filtered request', () => {
    const stale = new Subject<PagedResult<User>>();
    const fresh = new Subject<PagedResult<User>>();
    getPage.mockReturnValueOnce(stale).mockReturnValueOnce(fresh);
    const table = component();
    table.loadUsers(2); table.applyFilters();
    fresh.next(result(1, 1));
    stale.next(result(2, 65));
    stale.error(new HttpErrorResponse({ status: 500 }));
    expect(table.totalItems()).toBe(1);
    expect(table.currentPage()).toBe(1);
    expect(table.errorMessage()).toBe('');
  });

  it('shows loading and normalized error states', () => {
    const pending = new Subject<PagedResult<User>>();
    getPage.mockReturnValueOnce(pending);
    const table = component();
    table.loadUsers();
    expect(table.loading()).toBe(true);
    pending.error(new HttpErrorResponse({ status: 500 }));
    expect(table.loading()).toBe(false);
    expect(table.errorMessage()).not.toBe('');
  });

  it.each(['create', 'edit', 'deactivate', 'revoke'])('preserves %s and refreshes the current filtered page', action => {
    const table = component();
    table.canWrite = true; table.isActive = true;
    table.loadUsers(2);
    getPage.mockClear();
    if (action === 'create') {
      table.submitUserDialog({ mode: 'create', payload: { username: 'new-user', email: 'new@hfpos.test', password: 'Test-only-123', roleId: 2, establishmentId: 3, emissionPointId: 4 } });
      expect(create).toHaveBeenCalledOnce();
    }
    if (action === 'edit') {
      table.submitUserDialog({ mode: 'edit', id: user.id, payload: { email: user.email, roleId: 2, establishmentId: 3, emissionPointId: 4, isActive: true } });
      expect(update).toHaveBeenCalledOnce();
    }
    if (action === 'deactivate' || action === 'revoke') {
      if (action === 'deactivate') table.confirmDelete(user);
      else table.confirmRevokeSessions(user);
      (confirm.mock.calls[0][0].accept as () => void)();
      expect(action === 'deactivate' ? deactivate : revokeSessions).toHaveBeenCalledWith(user.id);
    }
    expect(getPage).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, isActive: true }));
  });

  it('reloads the last valid page after deactivation reduces the filtered total', () => {
    const table = component();
    table.canWrite = true; table.isActive = true;
    table.loadUsers(3);
    getPage.mockReturnValueOnce(of(result(3, 60))).mockReturnValueOnce(of(result(2, 60)));
    table.confirmDelete(user);
    (confirm.mock.calls[0][0].accept as () => void)();
    expect(getPage).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, isActive: true }));
    expect(table.first).toBe(30);
  });

  it('resets to page one for an empty result', () => {
    getPage.mockReturnValueOnce(of({ ...result(3, 0), items: [] }));
    const table = component();
    table.loadUsers(3);
    expect(table.first).toBe(0);
    expect(table.currentPage()).toBe(1);
    expect(table.users()).toEqual([]);
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
        { provide: UserService, useValue: { getPage: () => of({ items: [user], page: 1, pageSize: 30, totalItems: 1, totalPages: 1 }), revokeSessions } },
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
