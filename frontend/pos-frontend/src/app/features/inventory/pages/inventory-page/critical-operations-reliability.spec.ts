import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Observable, Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import { PermissionService } from '../../../../core/services/permission.service';
import { InventoryService } from '../../services/inventory.service';
import { InventoryMovement } from '../../models/inventory-movement.model';
import { InventoryOperationRequest } from '../../models/inventory-operation.model';
import { InventoryPage } from './inventory-page';
import { InventoryTransferPanel } from '../../components/inventory-transfer-panel/inventory-transfer-panel';
import { InventoryTransferCreateRequest, InventoryTransferDetail } from '../../models/inventory-transfer.model';
import { PurchaseReceiptsPage } from '../../../purchase-receipts/pages/purchase-receipts-page/purchase-receipts-page';
import { PurchaseReceiptService } from '../../../purchase-receipts/services/purchase-receipt.service';
import { ProductService } from '../../../catalog/services/product.service';
import { SupplierService } from '../../../suppliers/services/supplier.service';
import { CreatePurchaseReceiptRequest, PurchaseReceipt } from '../../../purchase-receipts/models/purchase-receipt.model';
import { CashSessionsPage } from '../../../cash-sessions/pages/cash-sessions-page/cash-sessions-page';
import { CashSessionService } from '../../../cash-sessions/services/cash-session.service';
import { CashSession } from '../../../cash-sessions/models/cash-session.model';
import { PaymentReconciliationPanel } from '../../../cash-sessions/components/payment-reconciliation-panel/payment-reconciliation-panel';
import { PaymentSettlementService } from '../../../cash-sessions/services/payment-settlement.service';
import { PaymentMethodActivity, PaymentReconciliation, PaymentSettlement } from '../../../cash-sessions/models/payment-settlement.model';

beforeEach(() => sessionStorage.clear());

function context() {
  const point = signal(3), company = signal(1), user = signal('10');
  const auth = { me: () => ({ userId: user() }), companyId: company,
    establishmentId: () => 2, emissionPointId: point, companyTimeZoneId: () => 'America/Guayaquil' };
  TestBed.configureTestingModule({ providers: [
    { provide: AuthStore, useValue: auth },
    { provide: PermissionService, useValue: { hasPermission: () => true } },
    { provide: MessageService, useValue: { add: vi.fn() } },
  ] });
  return { point, company, user };
}

const receipt = (id: number): PurchaseReceipt => ({ id, status: 1, items: [], supplierId: 4,
  supplierName: 'Synthetic', receiptNumber: null, supplierDocumentNumber: null,
  receiptDate: '2026-09-17', receiptBusinessDate: '2026-09-17', receiptTimeZoneIdSnapshot: 'America/Guayaquil',
  subtotal: 8, notes: null, createdAt: '2026-09-17T12:00:00Z', createdByUserId: 10,
  createdByUsername: 'Synthetic', postedAt: '2026-09-17T12:00:00Z', canceledAt: null,
  canceledBusinessDate: null, canceledTimeZoneIdSnapshot: null, canceledByUserId: null,
  canceledByUsername: null, cancelReason: null });
const cash = (id: number, status: 1 | 2 = 1): CashSession => ({ id, status, expectedCashAmount: 5, movements: [],
  companyId: 1, establishmentId: 2, emissionPointId: 3, openedByUserId: 10, openedByUsername: 'Synthetic',
  closedByUserId: null, closedByUsername: null, openingAmount: 5, countedCashAmount: status === 2 ? 7 : null,
  differenceAmount: status === 2 ? 2 : null, cashSalesAmount: 0, cardSalesAmount: 0,
  transferSalesAmount: 0, otherSalesAmount: 0, cashInAmount: 0, cashOutAmount: 0,
  openedAt: '2026-09-17T12:00:00Z', openBusinessDate: '2026-09-17', openTimeZoneIdSnapshot: 'America/Guayaquil',
  closedAt: status === 2 ? '2026-09-17T14:00:00Z' : null, closedBusinessDate: null,
  closedTimeZoneIdSnapshot: null, openingNotes: null, closingNotes: null, reconciliation: null });
const emptyPage = { items: [], page: 1, pageSize: 25, totalItems: 0, totalPages: 0 };

describe('529 receipt recovery and immutable cancellation target', () => {
  function setup() {
    const ctx = context();
    let response = new Subject<PurchaseReceipt>();
    const api = { getAll: vi.fn(() => of({ ...emptyPage, summary: { postedCount: 0, canceledCount: 0, totalReceived: 0 } })),
      getById: vi.fn((_id: number): Observable<PurchaseReceipt> => of(receipt(_id))),
      cancel: vi.fn((_id: number, _payload: unknown) => of(receipt(_id))),
      create: vi.fn((_payload: CreatePurchaseReceiptRequest) => response.asObservable()) };
    TestBed.configureTestingModule({ providers: [
      { provide: PurchaseReceiptService, useValue: api },
      { provide: ProductService, useValue: { lookup: () => of([]) } },
      { provide: SupplierService, useValue: { lookup: () => of([]) } },
    ] });
    const page = TestBed.runInInjectionContext(() => new PurchaseReceiptsPage());
    page.supplierId = 4; page.createDialogVisible = true;
    page.draftItems.set([{ uid: 1, productId: 7, quantity: 2, unitCost: 4, notes: 'Original' }]);
    return { page, api, ctx, get response() { return response; }, retryResponse() { response = new Subject(); return response; } };
  }

  it('blocks cancellation for selection from an earlier context', () => {
    const state = setup(); state.page.openDetail(receipt(2));
    state.ctx.point.set(4); state.page.openCancelDialog();
    expect(state.page.cancelDialogVisible).toBe(false);
    expect(state.api.cancel).not.toHaveBeenCalled();
  });

  it('blocks double submit and pending close, retries the exact payload after lost response despite edited draft', () => {
    const state = setup(); state.page.saveReceipt();
    const original = state.api.create.mock.calls[0][0];
    state.page.saveReceipt(); state.page.closeCreateDialog();
    expect(state.api.create).toHaveBeenCalledTimes(1); expect(state.page.createDialogVisible).toBe(true);
    state.response.error(new HttpErrorResponse({ status: 0 }));
    state.page.supplierId = 99; state.page.notes = 'Changed';
    state.page.draftItems.set([{ uid: 2, productId: 8, quantity: 10, unitCost: 19, notes: 'Changed' }]);
    const retry = state.retryResponse(); state.page.saveReceipt();
    expect(state.api.create.mock.calls[1][0]).toEqual(original);
    retry.next(receipt(42));
    expect(state.page.selectedReceipt()?.id).toBe(42); expect(state.page.receiptLocked()).toBe(false);
    expect(sessionStorage.length).toBe(0);
  });

  it('preserves an unresolved request across destroy/recreation, blocks a changed point, and never leaks to another actor', () => {
    const state = setup(); state.page.saveReceipt(); const original = state.api.create.mock.calls[0][0];
    state.page.ngOnDestroy(); state.response.next(receipt(42));
    expect(state.page.selectedReceipt()).toBeNull();
    state.ctx.point.set(8);
    const moved = TestBed.runInInjectionContext(() => new PurchaseReceiptsPage()); moved.saveReceipt();
    expect(state.api.create).toHaveBeenCalledTimes(1);
    expect(moved.formError()).toContain('contexto original');
    state.ctx.user.set('11');
    const other = TestBed.runInInjectionContext(() => new PurchaseReceiptsPage());
    expect(other.receiptLocked()).toBe(false);
    state.ctx.user.set('10'); state.ctx.point.set(3);
    const restored = TestBed.runInInjectionContext(() => new PurchaseReceiptsPage());
    state.retryResponse(); restored.saveReceipt();
    expect(state.api.create.mock.calls[1][0]).toEqual(original);
  });

  it('clears only a first definitive rejection, never an auth rejection after an unknown response', () => {
    const state = setup(); state.page.saveReceipt();
    state.response.error(new HttpErrorResponse({ status: 400 }));
    expect(state.page.receiptLocked()).toBe(false);
    state.retryResponse(); state.page.saveReceipt();
    state.response.error(new HttpErrorResponse({ status: 0 }));
    state.retryResponse(); state.page.saveReceipt();
    state.response.error(new HttpErrorResponse({ status: 403 }));
    expect(state.page.receiptLocked()).toBe(true);
  });

  it('late A detail cannot replace B cancellation target and closed/destroyed details ignore errors/results', () => {
    const state = setup(), a = new Subject<PurchaseReceipt>(), b = new Subject<PurchaseReceipt>();
    state.api.getById.mockReturnValueOnce(a).mockReturnValueOnce(b);
    state.page.openDetail(receipt(1)); state.page.openDetail(receipt(2)); b.next(receipt(2));
    state.page.openCancelDialog(); a.next(receipt(1));
    state.page.selectedReceipt.set(receipt(3)); // A subsequent selection is independent of the dialog intent.
    state.page.cancelReason = 'Synthetic reason'; state.page.confirmCancelReceipt();
    expect(state.api.cancel).toHaveBeenCalledWith(2, { reason: 'Synthetic reason' });
    expect(state.page.selectedReceipt()?.id).toBe(3);
    const late = new Subject<PurchaseReceipt>(); state.api.getById.mockReturnValueOnce(late);
    state.page.openDetail(receipt(4)); state.page.closeDetailDialog(); late.error(new HttpErrorResponse({ status: 500 }));
    expect(state.page.detailError()).toBe(''); expect(state.page.detailLoading()).toBe(false);
  });
});

describe('529 manual inventory and Kardex', () => {
  function setup() {
    const ctx = context(); let response = new Subject<InventoryMovement>();
    const api = { registerEntry: vi.fn((_payload: InventoryOperationRequest) => response.asObservable()),
      registerExit: vi.fn((_payload: InventoryOperationRequest) => response.asObservable()),
      registerAdjustment: vi.fn((_payload: InventoryOperationRequest) => response.asObservable()),
      getStocks: vi.fn(() => of({ ...emptyPage, summary: {} })),
      getMovements: vi.fn((_filters: unknown): Observable<typeof emptyPage & { items: InventoryMovement[] }> => of(emptyPage)),
      getMovementById: vi.fn((_id: number): Observable<InventoryMovement> => of({ id: _id } as InventoryMovement)),
      getCountSnapshot: vi.fn(() => of({ productId: 7, productName: 'Synthetic', quantity: 5,
        movementWatermark: 8, companyId: 1, establishmentId: 2 })),
      resolveError: vi.fn(() => 'Synthetic error') };
    TestBed.configureTestingModule({ providers: [{ provide: InventoryService, useValue: api }] });
    const page = TestBed.runInInjectionContext(() => new InventoryPage());
    return { page, api, ctx, get response() { return response; }, retryResponse() { response = new Subject(); return response; } };
  }

  it.each(['entry', 'exit', 'adjust'] as const)('recovers %s with frozen request and original count snapshot', kind => {
    const state = setup(); state.page.activeOperation = kind;
    state.page.setOperationProduct(7); state.page.setOperationQuantity(2);
    state.page.submitOperation();
    const api = kind === 'entry' ? state.api.registerEntry : kind === 'exit' ? state.api.registerExit : state.api.registerAdjustment;
    const original = api.mock.calls[0][0];
    state.page.submitOperation(); expect(api).toHaveBeenCalledTimes(1);
    state.response.error(new HttpErrorResponse({ status: 0 }));
    state.page.activeOperation = 'exit'; state.page.countSnapshot.set(null);
    state.page.entryForm.quantity = 90;
    const retry = state.retryResponse(); state.page.submitOperation();
    expect(api.mock.calls[1][0]).toEqual(original);
    retry.next({ id: 42, productId: 7, type: 1 } as InventoryMovement);
    expect(state.page.operationLocked()).toBe(false);
  });

  it('Kardex list is latest-wins for data, page, error and loading; destroy invalidates every response', () => {
    const state = setup(), a = new Subject<typeof emptyPage>(), b = new Subject<typeof emptyPage>();
    state.api.getMovements.mockReturnValueOnce(a).mockReturnValueOnce(b);
    state.page.loadMovements(1, 25); state.page.loadMovements(2, 25);
    a.error(new HttpErrorResponse({ status: 500 }));
    expect(state.page.movementsLoading()).toBe(true); expect(state.page.movementsError()).toBe('');
    b.next({ ...emptyPage, page: 2 }); expect(state.page.movementFirst).toBe(25);
    const late = new Subject<typeof emptyPage>(); state.api.getMovements.mockReturnValueOnce(late);
    state.page.loadMovements(3, 25); state.page.ngOnDestroy(); late.next({ ...emptyPage, page: 3 });
    expect(state.page.movementFirst).toBe(25);
  });

  it('Kardex detail ignores old A after B, close, error and destroy', () => {
    const state = setup(), a = new Subject<InventoryMovement>(), b = new Subject<InventoryMovement>();
    state.api.getMovementById.mockReturnValueOnce(a).mockReturnValueOnce(b);
    state.page.openMovementDetail({ id: 1 } as InventoryMovement);
    state.page.openMovementDetail({ id: 2 } as InventoryMovement);
    b.next({ id: 2 } as InventoryMovement); a.next({ id: 1 } as InventoryMovement);
    expect(state.page.selectedMovement()?.id).toBe(2);
    const closed = new Subject<InventoryMovement>(); state.api.getMovementById.mockReturnValueOnce(closed);
    state.page.openMovementDetail({ id: 3 } as InventoryMovement); state.page.onMovementDetailVisibleChange(false);
    closed.error(new HttpErrorResponse({ status: 500 }));
    expect(state.page.selectedMovement()).toBeNull(); expect(state.page.movementDetailError()).toBe('');
    expect(state.page.movementDetailLoading()).toBe(false);
  });
});

describe('529 cash recovery and frozen targets', () => {
  function setup() {
    const ctx = context(), movement = new Subject<CashSession>(), open = new Subject<CashSession>(), close = new Subject<CashSession>();
    const api = { getCurrent: vi.fn((): Observable<CashSession | null> => of(null)), getAll: vi.fn(() => of(emptyPage)),
      getById: vi.fn((id: number) => of(cash(id, 2))), open: vi.fn((_payload: unknown): Observable<CashSession> => open),
      close: vi.fn((_id: number, _payload: unknown) => close), addMovement: vi.fn((_id: number, _payload: unknown) => movement) };
    TestBed.configureTestingModule({ providers: [{ provide: CashSessionService, useValue: api }] });
    const page = TestBed.runInInjectionContext(() => new CashSessionsPage()); page.currentSession.set(cash(1));
    return { page, api, ctx, movement, open, close };
  }

  it('current GET generations cannot retarget movement and lost response keeps payload/session identity', () => {
    const state = setup(), a = new Subject<CashSession>(), b = new Subject<CashSession>();
    state.api.getCurrent.mockReturnValueOnce(a).mockReturnValueOnce(b);
    state.page.loadCurrent(); state.page.loadCurrent(); b.next(cash(2));
    state.page.openMovementDialog(1); a.next(cash(1)); state.page.currentSession.set(cash(3));
    state.page.movementAmount = 2; state.page.movementReason = 'Original';
    state.page.confirmMovement(); state.page.confirmMovement();
    expect(state.api.addMovement).toHaveBeenCalledTimes(1); expect(state.api.addMovement.mock.calls[0][0]).toBe(2);
    const payload = state.api.addMovement.mock.calls[0][1];
    state.movement.error(new HttpErrorResponse({ status: 0 }));
    state.page.movementAmount = 99; state.page.movementReason = 'Changed';
    state.api.addMovement.mockReturnValueOnce(new Subject<CashSession>());
    state.page.confirmMovement(); expect(state.api.addMovement.mock.calls[1]).toEqual([2, payload]);
  });

  it('unknown opening recovers the same key after the original closed and another session opened', () => {
    const state = setup(); state.page.currentSession.set(null);
    state.page.openCashDialog(); state.page.confirmOpen();
    const payload = state.api.open.mock.calls[0][0];
    state.open.error(new HttpErrorResponse({ status: 0 }));
    state.page.openingAmount = 99;
    state.api.open.mockReturnValueOnce(of(cash(1, 2)));
    state.api.getCurrent.mockReturnValueOnce(of(cash(2)));
    state.page.confirmOpen();
    expect(state.api.open.mock.calls[1][0]).toEqual(payload);
    expect(state.page.selectedSession()?.id).toBe(1);
    expect(state.page.currentSession()?.id).toBe(2);
    expect(state.page.openLocked()).toBe(false);
  });

  it('close recovery queries original id and displays authoritative registered values without retargeting', () => {
    const state = setup(); state.page.openCloseDialog(); state.page.countedCashAmount = 4;
    state.page.currentSession.set(cash(99)); state.page.confirmClose();
    expect(state.api.close.mock.calls[0][0]).toBe(1);
    state.close.error(new HttpErrorResponse({ status: 0 }));
    state.page.confirmClose();
    expect(state.api.getById).toHaveBeenCalledWith(1); expect(state.api.close).toHaveBeenCalledTimes(1);
    expect(state.page.selectedSession()?.id).toBe(1); expect(state.page.closeLocked()).toBe(false);
  });
});

describe('529 Transfer intent', () => {
  it('preserves the existing UUID and exact payload after lost response, navigation and context change', () => {
    const ctx = context(), response = new Subject<InventoryTransferDetail>();
    const api = { createTransfer: vi.fn((_payload: InventoryTransferCreateRequest) => response),
      resolveError: () => 'Respuesta desconocida' };
    TestBed.configureTestingModule({ providers: [{ provide: InventoryService, useValue: api }] });
    const panel = TestBed.runInInjectionContext(() => new InventoryTransferPanel()); panel.canWrite = true;
    panel.destinationId = 4; panel.destinations.set([{ id: 4, name: 'Synthetic' }]);
    panel.products.set([{ productId: 7, productName: 'Synthetic', quantity: 10,
      isActive: true, barcode: null, internalCode: null }]);
    panel.lines = [{ id: 1, productId: 7, quantity: 2 }];
    panel.submit(); panel.submit(); expect(api.createTransfer).toHaveBeenCalledTimes(1);
    const original = api.createTransfer.mock.calls[0][0];
    response.error(new HttpErrorResponse({ status: 0 })); panel.ngOnDestroy();
    const recovered = TestBed.runInInjectionContext(() => new InventoryTransferPanel()); recovered.canWrite = true;
    recovered.destinationId = 99; recovered.lines = [];
    ctx.point.set(8); recovered.submit(); expect(api.createTransfer).toHaveBeenCalledTimes(1);
    ctx.point.set(3); recovered.submit(); expect(api.createTransfer.mock.calls[1][0]).toEqual(original);
    expect(recovered.intentLocked()).toBe(true);
  });
});

describe('529 Settlement intent', () => {
  it('does not submit a method selected in a different context before the first POST', () => {
    const ctx = context();
    const method = { paymentMethod: 1, canSettle: true, netPaymentAmount: 10 } as PaymentMethodActivity;
    const api = { create: vi.fn(), getReconciliation: () => of({ businessDate: '2026-09-17', methods: [method] } as PaymentReconciliation) };
    TestBed.configureTestingModule({ providers: [{ provide: PaymentSettlementService, useValue: api }] });
    const panel = TestBed.runInInjectionContext(() => new PaymentReconciliationPanel()); panel.canWrite = true;
    panel.loadOverview();
    panel.openSettlement(method); ctx.point.set(4); panel.confirmSettlement();
    expect(api.create).not.toHaveBeenCalled(); expect(panel.formError()).toContain('contexto original');
  });
  it('freezes selected business date and method despite later overview and preserves unknown payload across context changes', () => {
    const ctx = context(), response = new Subject<PaymentSettlement>(), late = new Subject<PaymentReconciliation>();
    const method = { paymentMethod: 1, canSettle: true, netPaymentAmount: 10 } as PaymentMethodActivity;
    const api = { getReconciliation: vi.fn((): Observable<PaymentReconciliation> => of({ businessDate: '2026-09-17', methods: [method] } as PaymentReconciliation)), getAll: vi.fn(() => of(emptyPage)),
      create: vi.fn((_request: unknown): Observable<PaymentSettlement> => response) };
    TestBed.configureTestingModule({ providers: [{ provide: PaymentSettlementService, useValue: api }] });
    const panel = TestBed.runInInjectionContext(() => new PaymentReconciliationPanel()); panel.canWrite = true;
    panel.loadOverview(); panel.openSettlement(method);
    api.getReconciliation.mockReturnValueOnce(late); panel.loadOverview();
    late.next({ businessDate: '2026-09-18', methods: [method] } as PaymentReconciliation);
    panel.confirmSettlement();
    const original = api.create.mock.calls[0][0];
    expect(original).toMatchObject({ businessDate: '2026-09-17', paymentMethod: 1 });
    response.error(new HttpErrorResponse({ status: 0 }));
    panel.settledAmount = 99; panel.draftChanged();
    ctx.point.set(8); panel.confirmSettlement(); expect(api.create).toHaveBeenCalledTimes(1);
    ctx.point.set(3); api.create.mockReturnValueOnce(new Subject<PaymentSettlement>()); panel.confirmSettlement();
    expect(api.create.mock.calls[1][0]).toEqual(original);
  });
});
