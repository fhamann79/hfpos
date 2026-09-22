import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { CashSessionStatus } from '../../models/cash-session.model';
import { CashSessionService } from '../../services/cash-session.service';
import { CashSessionsPage } from './cash-sessions-page';

describe('CashSessionsPage server-side pagination', () => {
  let component: CashSessionsPage;
  let service: {
    getCurrent: ReturnType<typeof vi.fn>;
    getAll: ReturnType<typeof vi.fn>;
    getById: ReturnType<typeof vi.fn>;
    open: ReturnType<typeof vi.fn>;
    addMovement: ReturnType<typeof vi.fn>;
    close: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      getCurrent: vi.fn(() => of(null)),
      getAll: vi.fn(() =>
        of({
          items: [],
          page: 1,
          pageSize: 15,
          totalItems: 41,
          totalPages: 3,
          summary: { openCount: 6, closedCount: 35 },
        })
      ),
      getById: vi.fn(),
      open: vi.fn(),
      addMovement: vi.fn(),
      close: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: CashSessionService, useValue: service },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
    component = TestBed.runInInjectionContext(() => new CashSessionsPage());
  });

  it('loads current session independently from the bounded history and uses global counts', () => {
    component.ngOnInit();

    expect(service.getCurrent).toHaveBeenCalledOnce();
    expect(service.getAll).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 15 }));
    expect(component.totalItems()).toBe(41);
    expect(component.totalPages()).toBe(3);
    expect(component.totalOpenSessions()).toBe(6);
    expect(component.totalClosedSessions()).toBe(35);
  });

  it('loads lazy pages and resets to page one when filters are applied or cleared', () => {
    component.onSessionsLazyLoad({ first: 30, rows: 15 });
    expect(service.getAll).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 3, pageSize: 15 })
    );

    component.status = CashSessionStatus.Closed;
    component.userId = 9;
    component.currentPage.set(3);
    component.first = 30;
    component.applyFilters();
    expect(service.getAll).toHaveBeenLastCalledWith(
      expect.objectContaining({
        page: 1,
        pageSize: 15,
        status: CashSessionStatus.Closed,
        userId: 9,
      })
    );
    expect(component.first).toBe(0);

    component.clearFilters();
    expect(service.getAll).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 1, status: null, userId: null })
    );
  });
});
