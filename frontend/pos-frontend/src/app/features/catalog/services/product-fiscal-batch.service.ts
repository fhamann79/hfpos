import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { PagedResult } from '../../../core/models/paged-result.model';
import { DeclareProductFiscalClassification, ProductFiscalClassification } from '../../fiscal-settings/models/fiscal-profile.model';

export type FiscalStatus = 'pending' | 'stale' | 'current';
export interface FiscalBatchCandidate {
  id: number; name: string; internalCode: string | null; barcode: string | null; categoryId: number;
  isActive: boolean; vatCategory: number; fiscalStatus: FiscalStatus;
}
export interface FiscalBatchQuery {
  search: string; active: boolean | null; categoryId: number | null; fiscalStatus: FiscalStatus | null; page: number;
}
interface FiscalBatchDecision { category: number; rate: number; reason: string }
export interface FiscalBatchRow {
  productId: number; name: string | null; internalCode: string | null;
  currentVat: number | null; proposedVat: number | null;
  currentClassification: ProductFiscalClassification | null; proposedClassification: ProductFiscalClassification | null;
  currentDecision: FiscalBatchDecision | null; proposedDecision: FiscalBatchDecision | null;
  currentIssue: string | null; outcome: 'changed' | 'unchanged' | 'rejected';
  rejection: string | null; availabilityWarnings: string[];
}
interface FiscalBatchReport {
  operationId: string; companyId: number; companyName: string; fiscalDate: string;
  reason: string; rows: FiscalBatchRow[]; changed: number; unchanged: number; rejected: number;
}
export interface FiscalBatchPreview extends FiscalBatchReport {
  declaration: DeclareProductFiscalClassification;
  previewToken: string | null; expiresAt: string | null;
}
export interface FiscalBatchResult extends FiscalBatchReport {
  userId: number; appliedAt: string; profileRevision: number;
}

@Injectable({ providedIn: 'root' })
export class ProductFiscalBatchService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/products/fiscal-batches`;

  candidates(query: FiscalBatchQuery) {
    let params = new HttpParams().set('page', query.page);
    if (query.search.trim()) params = params.set('search', query.search.trim());
    if (query.categoryId !== null) params = params.set('categoryId', query.categoryId);
    if (query.active !== null) params = params.set('active', query.active);
    if (query.fiscalStatus) params = params.set('fiscalStatus', query.fiscalStatus);
    return this.http.get<PagedResult<FiscalBatchCandidate>>(`${this.base}/candidates`, { params });
  }

  preview(productIds: number[], declaration: DeclareProductFiscalClassification, reason: string) {
    return this.http.post<FiscalBatchPreview>(`${this.base}/preview`, { productIds, declaration, reason });
  }

  confirm(operationId: string, previewToken: string) {
    return this.http.post<FiscalBatchResult>(`${this.base}/confirm`, { operationId, previewToken, confirmed: true });
  }

  receipt(operationId: string) {
    return this.http.get<FiscalBatchResult>(`${this.base}/${encodeURIComponent(operationId)}`);
  }
}
