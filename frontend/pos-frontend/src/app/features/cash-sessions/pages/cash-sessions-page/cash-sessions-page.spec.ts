import { TestBed } from '@angular/core/testing';

beforeEach(() => sessionStorage.clear());
import { MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { CashSession, CashSessionStatus } from '../../models/cash-session.model';
import { CashSessionService } from '../../services/cash-session.service';
import { PaymentSettlementService } from '../../services/payment-settlement.service';
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
        { provide: PaymentSettlementService, useValue: {
          getReconciliation: () => of({ businessDate: '2026-09-17', currentBusinessDate: '2026-09-18',
            legacyUnattributedVoidCount: 0, methods: [] }),
          getAll: () => of({ items: [], page: 1, pageSize: 15, totalItems: 0, totalPages: 0 }),
        } },
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

  it('renders the cash reconciliation breakdown in session detail', () => {
    const fixture = TestBed.createComponent(CashSessionsPage);
    fixture.componentInstance.selectedSession.set({
      id: 1, status: CashSessionStatus.Closed, openingAmount: 20,
      companyId: 1, establishmentId: 1, emissionPointId: 1,
      openedByUserId: 1, closedByUserId: 1, closedByUsername: 'tester',
      cashSalesAmount: 10, cardSalesAmount: 0, transferSalesAmount: 0, otherSalesAmount: 0,
      cashInAmount: 3, cashOutAmount: 2, expectedCashAmount: 31,
      countedCashAmount: 30, differenceAmount: -1,
      openBusinessDate: '2026-09-17', openedAt: '2026-09-17T12:00:00Z',
      openTimeZoneIdSnapshot: 'America/Guayaquil', openedByUsername: 'tester',
      closedAt: '2026-09-17T13:00:00Z', closedBusinessDate: '2026-09-17',
      closedTimeZoneIdSnapshot: 'America/Guayaquil', openingNotes: null, closingNotes: null,
      movements: [],
      reconciliation: {
        grossCashSalesAmount: 15, inSessionVoidAmount: 5, netCashSalesAmount: 10,
        manualCashInAmount: 3, manualCashOutAmount: 2,
        saleVoidCashOutAmount: 0, creditNoteRefundCashOutAmount: 0,
        expectedCashAmount: 31, countedCashAmount: 30, differenceAmount: -1,
        isReconstructionComplete: true,
      },
    } satisfies CashSession);
    fixture.componentInstance.detailDialogVisible = true;
    fixture.detectChanges();
    const text = document.body.textContent ?? '';
    expect(text).toContain('Conciliación de efectivo');
    expect(text).toContain('Ventas efectivo brutas');
    expect(text).toContain('Salidas por devoluciones');
    expect(text).toContain('Efectivo contado');
    fixture.componentInstance.selectedSession.update((session) => ({ ...session!,
      reconciliation: { ...session!.reconciliation!, isReconstructionComplete: false } }));
    fixture.detectChanges();
    expect(document.body.textContent).toContain('no puede reconstruirse por completo');
  });
});
