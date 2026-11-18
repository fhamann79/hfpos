import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { ProductVatCategory } from '../../../core/utils/vat-category';

export type FiscalStatus = 'pending' | 'stale' | 'current';
export interface FiscalBatchCandidate {
  id: number; name: string; internalCode: string | null; barcode: string | null; categoryId: number;
  isActive: boolean; vatCategory: ProductVatCategory; fiscalStatus: FiscalStatus;
}
export interface FiscalBatchQuery {
  search: string; active: boolean | null; categoryId: number | null; fiscalStatus: FiscalStatus | null;
  vatCategory: ProductVatCategory | null;
}
interface FiscalBatchCandidates {
  totalItems: number; maxProducts: number; items: FiscalBatchCandidate[];
}
export interface PriceBatchReceiptSummary {
  operationId: string; appliedAt: string; rowCount: number;
}
export type PriceBatchAction = 'increase-percent' | 'decrease-percent' | 'increase-amount' | 'decrease-amount' | 'set-price' | 'gross-margin';
export interface PriceBatchRow {
  productId: number; name: string | null; internalCode: string | null;
  currentPrice: number | null; cost: number | null; proposedPrice: number | null;
  currentMargin: string | null; proposedMargin: string | null;
  outcome: 'changed' | 'unchanged' | 'rejected'; rejection: string | null; warnings: string[];
}
interface PriceBatchReport {
  operationId: string; companyId: number; companyName: string; action: PriceBatchAction; value: number;
  reason: string; rows: PriceBatchRow[]; changed: number; unchanged: number; rejected: number;
}
export interface PriceBatchPreview extends PriceBatchReport {
  previewToken: string | null; expiresAt: string | null;
}
export interface PriceBatchResult extends PriceBatchReport { userId: number; appliedAt: string; }

@Injectable({ providedIn: 'root' })
export class ProductPriceBatchService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/products/price-batches`;

  candidates(query: FiscalBatchQuery) {
    let params = new HttpParams();
    if (query.search.trim()) params = params.set('search', query.search.trim());
    if (query.categoryId !== null) params = params.set('categoryId', query.categoryId);
    if (query.active !== null) params = params.set('active', query.active);
    if (query.fiscalStatus) params = params.set('fiscalStatus', query.fiscalStatus);
    if (query.vatCategory !== null) params = params.set('vatCategory', query.vatCategory);
    return this.http.get<FiscalBatchCandidates>(`${this.base}/candidates`, { params });
  }

  preview(productIds: number[], action: PriceBatchAction, value: number, reason: string) {
    return this.http.post<PriceBatchPreview>(`${this.base}/preview`, { productIds, action, value, reason });
  }

  confirm(operationId: string, previewToken: string) {
    return this.http.post<PriceBatchResult>(`${this.base}/confirm`, { operationId, previewToken, confirmed: true });
  }

  receipt(operationId: string) {
    return this.http.get<PriceBatchResult>(`${this.base}/${encodeURIComponent(operationId)}`);
  }

  recentReceipts() {
    return this.http.get<PriceBatchReceiptSummary[]>(`${this.base}/recent`);
  }
}
