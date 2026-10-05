import { TestBed } from '@angular/core/testing';

beforeEach(() => sessionStorage.clear());
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { PaymentMethodActivity, PaymentReconciliation, PaymentSettlement,
  SettlementPaymentMethod } from '../../models/payment-settlement.model';
import { PaymentSettlementService } from '../../services/payment-settlement.service';
import { PaymentReconciliationPanel } from './payment-reconciliation-panel';

const methods: PaymentMethodActivity[] = [0, 1, 2, 3].map((paymentMethod) => ({
  paymentMethod: paymentMethod as SettlementPaymentMethod,
  grossSalesAmount: 20,
  voidAmount: 5,
  refundAmount: 25,
  netPaymentAmount: -10,
  canSettle: paymentMethod !== 0,
  settlement: null,
}));
const overview: PaymentReconciliation = {
  businessDate: '2026-09-17', currentBusinessDate: '2026-09-18',
  legacyUnattributedVoidCount: 2, methods,
};
const page = { items: [], page: 1, pageSize: 15, totalItems: 0, totalPages: 0 };

describe('PaymentReconciliationPanel', () => {
  let component: PaymentReconciliationPanel;
  let service: {
    getReconciliation: ReturnType<typeof vi.fn>;
    getAll: ReturnType<typeof vi.fn>;
    getById: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    service = {
      getReconciliation: vi.fn(() => of(overview)),
      getAll: vi.fn(() => of(page)),
      getById: vi.fn(),
      create: vi.fn(),
    };
    TestBed.configureTestingModule({ providers: [{ provide: PaymentSettlementService, useValue: service }] });
    component = TestBed.runInInjectionContext(() => new PaymentReconciliationPanel());
    component.canWrite = true;
    component.ngOnInit();
  });

  it('shows four methods, financial components, cash without action, and legacy warning', () => {
    expect(component.overview()?.methods).toHaveLength(4);
    expect(component.overview()?.methods[1]).toMatchObject({ grossSalesAmount: 20,
      voidAmount: 5, refundAmount: 25, netPaymentAmount: -10 });
    expect(component.overview()?.legacyUnattributedVoidCount).toBe(2);
    component.openSettlement(methods[0]);
    expect(component.settleDialogVisible).toBe(false);
    component.openSettlement(methods[1]);
    expect(component.settleDialogVisible).toBe(true);
    expect(component.settledAmount).toBe(-10);
  });

  it('renders the financial table and the legacy warning without a Cash settlement button', () => {
    const fixture = TestBed.createComponent(PaymentReconciliationPanel);
    fixture.componentInstance.canWrite = true;
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Conciliación de pagos');
    expect(text).toContain('Efectivo');
    expect(text).toContain('Tarjeta');
    expect(text).toContain('Transferencia');
    expect(text).toContain('Otros');
    expect(text).toContain('Bruto');
    expect(text).toContain('Anulaciones');
    expect(text).toContain('Devoluciones');
    expect(text).toContain('Por caja');
    expect(text).toContain('Existen anulaciones históricas');
    expect(fixture.nativeElement.querySelectorAll('button').length).toBeGreaterThan(0);
  });

  it('uses backend business date, paginates history, and blocks current-day settlement', () => {
    expect(component.businessDate).toBe('2026-09-17');
    component.onHistoryLazyLoad({ first: 30, rows: 15 });
    expect(service.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ page: 3, pageSize: 15 }));
    component.historyMethod = SettlementPaymentMethod.Card;
    component.applyHistoryFilters();
    expect(service.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1, paymentMethod: 1 }));
    service.getReconciliation.mockReturnValue(of({ ...overview, businessDate: '2026-09-18',
      methods: methods.map((method) => ({ ...method, canSettle: false })) }));
    component.businessDate = '2026-09-18';
    component.changeDate();
    component.openSettlement(component.overview()!.methods[1]);
    expect(component.settleDialogVisible).toBe(false);
  });

  it('keeps RequestId on retry, changes it with draft and blocks double click', () => {
    component.openSettlement(methods[1]);
    const pending = new Subject<PaymentSettlement>();
    service.create.mockReturnValueOnce(pending.asObservable());
    component.confirmSettlement();
    const firstRequest = service.create.mock.calls[0][0];
    expect(firstRequest.settledAmount).toBe(-10);
    expect(firstRequest.requestId).toBeTruthy();
    component.confirmSettlement();
    expect(service.create).toHaveBeenCalledTimes(1);
    pending.error(new HttpErrorResponse({ status: 409, error: { error: 'PAYMENT_SETTLEMENT_ALREADY_RECONCILED' } }));
    service.create.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409,
      error: { error: 'PAYMENT_SETTLEMENT_ALREADY_RECONCILED' } })));
    component.confirmSettlement();
    expect(service.create.mock.calls[1][0].requestId).toBe(firstRequest.requestId);
    component.settledAmount = -12;
    component.draftChanged();
    expect(component.differencePreview).toBe(-2);
    service.create.mockReturnValue(of({ id: 1 } as PaymentSettlement));
    component.confirmSettlement();
    expect(service.create.mock.calls[2][0].requestId).not.toBe(firstRequest.requestId);
    expect(service.getReconciliation).toHaveBeenCalledTimes(2);
    expect(service.getAll).toHaveBeenCalledTimes(2);
  });

  it('accepts negative, zero and positive differences and is read-only without write permission', () => {
    component.openSettlement(methods[2]);
    component.settledAmount = -11;
    expect(component.differencePreview).toBe(-1);
    component.settledAmount = -10;
    expect(component.differencePreview).toBe(0);
    component.settledAmount = -9;
    expect(component.differencePreview).toBe(1);
    component.canWrite = false;
    component.confirmSettlement();
    expect(service.create).not.toHaveBeenCalled();
    component.closeSettlement();
    component.openSettlement(methods[2]);
    expect(component.settleDialogVisible).toBe(false);
  });

  it('normalizes backend errors and refuses invalid precision without HTTP', () => {
    component.openSettlement(methods[3]);
    component.settledAmount = 0.001;
    component.confirmSettlement();
    expect(service.create).not.toHaveBeenCalled();
    component.settledAmount = 0;
    service.create.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400,
      error: { error: 'PAYMENT_SETTLEMENT_DATE_NOT_FINAL' } })));
    component.confirmSettlement();
    expect(component.formError()).toContain('fechas de negocio anteriores');
  });
});
