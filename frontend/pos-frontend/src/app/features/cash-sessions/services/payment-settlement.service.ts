import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { operationResult } from '../../../core/utils/operation-intent';
import { environment } from '../../../../environments/environment';
import {
  CreatePaymentSettlementRequest,
  PaymentReconciliation,
  PaymentSettlement,
  PaymentSettlementFilters,
  PaymentSettlementPage,
} from '../models/payment-settlement.model';

@Injectable({ providedIn: 'root' })
export class PaymentSettlementService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/PaymentSettlements`;

  getReconciliation(businessDate?: string): Observable<PaymentReconciliation> {
    const params = businessDate ? new HttpParams().set('businessDate', businessDate) : new HttpParams();
    return this.http.get<PaymentReconciliation>(`${this.baseUrl}/reconciliation`, { params });
  }

  getAll(filters: PaymentSettlementFilters): Observable<PaymentSettlementPage> {
    let params = new HttpParams()
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 15);
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.paymentMethod !== null && filters.paymentMethod !== undefined)
      params = params.set('paymentMethod', filters.paymentMethod);
    return this.http.get<PaymentSettlementPage>(this.baseUrl, { params });
  }

  getById(id: number): Observable<PaymentSettlement> {
    return this.http.get<PaymentSettlement>(`${this.baseUrl}/${id}`);
  }

  create(request: CreatePaymentSettlementRequest): Observable<PaymentSettlement> {
    return this.http.post<PaymentSettlement>(this.baseUrl, request).pipe(map(value => operationResult(value, request.requestId)));
  }
}
