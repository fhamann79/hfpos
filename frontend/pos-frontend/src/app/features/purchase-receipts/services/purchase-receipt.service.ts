import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { operationResult } from '../../../core/utils/operation-intent';
import { environment } from '../../../../environments/environment';
import { PagedResultWithSummary } from '../../../core/models/paged-result.model';
import {
  CancelPurchaseReceiptRequest,
  CreatePurchaseReceiptRequest,
  PurchaseReceipt,
  PurchaseReceiptExportFilters,
  PurchaseReceiptFilters,
  PurchaseReceiptListItem,
  PurchaseReceiptSummary,
} from '../models/purchase-receipt.model';

@Injectable({ providedIn: 'root' })
export class PurchaseReceiptService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/PurchaseReceipts`;

  getAll(
    filters: PurchaseReceiptFilters = {}
  ): Observable<PagedResultWithSummary<PurchaseReceiptListItem, PurchaseReceiptSummary>> {
    const params = this.filterParams(filters)
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 50);

    return this.http.get<PagedResultWithSummary<PurchaseReceiptListItem, PurchaseReceiptSummary>>(
      this.baseUrl,
      { params }
    );
  }

  exportCsv(filters: PurchaseReceiptExportFilters = {}): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}/export`, {
      params: this.filterParams(filters),
      responseType: 'blob',
      observe: 'response',
    });
  }

  private filterParams(filters: PurchaseReceiptExportFilters): HttpParams {
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

    return params;
  }

  getById(id: number) {
    return this.http.get<PurchaseReceipt>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreatePurchaseReceiptRequest) {
    return this.http.post<PurchaseReceipt>(this.baseUrl, payload).pipe(map(value => operationResult(value, payload.requestId)));
  }

  cancel(id: number, payload: CancelPurchaseReceiptRequest) {
    return this.http.post<PurchaseReceipt>(`${this.baseUrl}/${id}/cancel`, payload);
  }
}
