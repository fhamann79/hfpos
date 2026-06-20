import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  CancelPurchaseReceiptRequest,
  CreatePurchaseReceiptRequest,
  PurchaseReceipt,
  PurchaseReceiptFilters,
  PurchaseReceiptListItem,
} from '../models/purchase-receipt.model';

@Injectable({ providedIn: 'root' })
export class PurchaseReceiptService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/PurchaseReceipts`;

  getAll(filters: PurchaseReceiptFilters = {}) {
    let params = new HttpParams();

    const search = filters.search?.trim();
    if (search) {
      params = params.set('search', search);
    }

    if (filters.from) {
      params = params.set('from', filters.from);
    }

    if (filters.to) {
      params = params.set('to', filters.to);
    }

    if (filters.status !== null && filters.status !== undefined) {
      params = params.set('status', filters.status);
    }

    return this.http.get<PurchaseReceiptListItem[]>(this.baseUrl, { params });
  }

  getById(id: number) {
    return this.http.get<PurchaseReceipt>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreatePurchaseReceiptRequest) {
    return this.http.post<PurchaseReceipt>(this.baseUrl, payload);
  }

  cancel(id: number, payload: CancelPurchaseReceiptRequest) {
    return this.http.post<PurchaseReceipt>(`${this.baseUrl}/${id}/cancel`, payload);
  }
}
