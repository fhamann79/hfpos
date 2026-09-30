import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SettlementPaymentMethod } from '../models/payment-settlement.model';
import { PaymentSettlementService } from './payment-settlement.service';

describe('PaymentSettlementService', () => {
  let service: PaymentSettlementService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PaymentSettlementService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reads the backend default business date and filters history server-side', () => {
    service.getReconciliation().subscribe();
    const overview = http.expectOne((request) => request.url.endsWith('/api/PaymentSettlements/reconciliation'));
    expect(overview.request.params.keys()).toEqual([]);
    overview.flush({ businessDate: '2026-09-17', currentBusinessDate: '2026-09-18',
      legacyUnattributedVoidCount: 0, methods: [] });

    service.getAll({ from: '2026-09-01', to: '2026-09-17',
      paymentMethod: SettlementPaymentMethod.Transfer, page: 2, pageSize: 15 }).subscribe();
    const history = http.expectOne((request) => request.url.endsWith('/api/PaymentSettlements'));
    expect(history.request.params.get('paymentMethod')).toBe('2');
    expect(history.request.params.get('page')).toBe('2');
    expect(history.request.params.get('pageSize')).toBe('15');
    expect(history.request.params.get('from')).toBe('2026-09-01');
    history.flush({ items: [], page: 2, pageSize: 15, totalItems: 22, totalPages: 2 });
  });

  it('sends only the operator-entered amount and idempotency key', () => {
    service.create({ requestId: 'synthetic-uuid', businessDate: '2026-09-17',
      paymentMethod: SettlementPaymentMethod.Card, settledAmount: -10,
      reference: null, notes: null }).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/PaymentSettlements'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ requestId: 'synthetic-uuid', businessDate: '2026-09-17',
      paymentMethod: 1, settledAmount: -10, reference: null, notes: null });
    expect(request.request.body.expectedNetAmount).toBeUndefined();
    request.flush({ id: 1 });
  });
});
