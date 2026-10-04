import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SalePaymentMethod } from '../models/sale-payment-method.model';
import { SaleVoidCashEffect } from '../models/sale-void-cash-effect.model';
import { PosWorkstationService } from './pos-workstation.service';

describe('PosWorkstationService sale void mapping', () => {
  let service: PosWorkstationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PosWorkstationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('keeps receipt snapshots and tendered/change from the authoritative POST response', () => {
    const payload = { requestId: crypto.randomUUID(), cashReceived: 20, items: [{ productId: 7, quantity: 1, unitPrice: 10 }] };
    service.createSale(payload).subscribe(sale => {
      expect(sale.requestId).toBe(payload.requestId); expect(sale.cashReceived).toBe(20); expect(sale.cashChange).toBe(10);
      expect(sale.items[0].productNameSnapshot).toBe('Synthetic snapshot'); expect(sale.items[0].productSkuSnapshot).toBe('SYN7');
    });
    const request = http.expectOne(candidate => candidate.url.endsWith('/api/Sales'));
    expect(request.request.body).toEqual(payload);
    request.flush({ id: 42, requestId: payload.requestId, total: 10, cashReceived: 20, cashChange: 10,
      items: [{ productId: 7, productNameSnapshot: 'Synthetic snapshot', productSkuSnapshot: 'SYN7' }] });
  });

  it('treats malformed successful HTTP responses as ambiguous instead of inventing a sale', () => {
    service.createSale({ requestId: crypto.randomUUID(), items: [{ productId: 7, quantity: 1, unitPrice: 10 }] })
      .subscribe({ next: () => { throw new Error('Must not accept malformed sale'); }, error: error => expect(error.status).toBe(502) });
    http.expectOne(candidate => candidate.url.endsWith('/api/Sales')).flush({ id: 42, total: 'bad', items: [] });
  });

  it('keeps payment method in the recent-sales list', () => {
    service.getSales().subscribe((result) => {
      expect(result.items[0].paymentMethod).toBe(SalePaymentMethod.Transfer);
      expect(result.totalItems).toBe(81);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Sales'));
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('50');
    expect(request.request.params.has('includeSummary')).toBe(false);
    request.flush({
      items: [{ id: 10, status: 1, paymentMethod: SalePaymentMethod.Transfer }],
      page: 1,
      pageSize: 50,
      totalItems: 81,
      totalPages: 2,
    });
  });

  it('maps structured void audit and cash links from sale detail', () => {
    service.getSaleDetail(42).subscribe((sale) => {
      expect(sale.paymentMethod).toBe(SalePaymentMethod.Cash);
      expect(sale.cashSessionId).toBe(7);
      expect(sale.voidCashEffect).toBe(SaleVoidCashEffect.CurrentSessionCashOut);
      expect(sale.voidedByUsername).toBe('cashier');
      expect(sale.voidReason).toBe('Error de cobro');
      expect(sale.voidBusinessDate).toBe('2026-09-18');
      expect(sale.voidTimeZoneIdSnapshot).toBe('America/Guayaquil');
      expect(sale.voidCashSessionId).toBe(9);
      expect(sale.voidCashMovementId).toBe(15);
    });

    http.expectOne((request) => request.url.endsWith('/api/Sales/42')).flush({
      id: 42,
      status: 2,
      paymentMethod: 0,
      cashSessionId: 7,
      voidedAt: '2026-09-18T14:00:00Z',
      voidedByUserId: 3,
      voidedByUsername: 'cashier',
      voidReason: 'Error de cobro',
      voidBusinessDate: '2026-09-18',
      voidTimeZoneIdSnapshot: 'America/Guayaquil',
      voidCashEffect: 2,
      voidCashSessionId: 9,
      voidCashMovementId: 15,
      items: [],
    });
  });

  it('maps the updated sale returned by the void endpoint', () => {
    service.voidSale(42, { reason: 'Duplicada' }).subscribe((sale) => {
      expect(sale.isVoided).toBe(true);
      expect(sale.voidCashEffect).toBe(SaleVoidCashEffect.NoCashMovement);
    });

    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/Sales/42/void'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Duplicada' });
    request.flush({ id: 42, status: 2, paymentMethod: 1, voidCashEffect: 3, items: [] });
  });
});
