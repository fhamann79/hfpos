import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResultWithSummary } from '../../../core/models/paged-result.model';
import { environment } from '../../../../environments/environment';
import {
  CashSession,
  CashSessionFilters,
  CashSessionListItem,
  CashSessionSummary,
  CloseCashSessionRequest,
  CreateCashMovementRequest,
  OpenCashSessionRequest,
} from '../models/cash-session.model';

@Injectable({ providedIn: 'root' })
export class CashSessionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/CashSessions`;

  getCurrent(): Observable<CashSession | null> {
    return this.http.get<CashSession | null>(`${this.baseUrl}/current`);
  }

  getAll(
    filters: CashSessionFilters = {}
  ): Observable<PagedResultWithSummary<CashSessionListItem, CashSessionSummary>> {
    let params = new HttpParams()
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 50);

    if (filters.from) {
      params = params.set('from', filters.from);
    }

    if (filters.to) {
      params = params.set('to', filters.to);
    }

    if (filters.status !== null && filters.status !== undefined) {
      params = params.set('status', filters.status);
    }

    if (filters.userId !== null && filters.userId !== undefined) {
      params = params.set('userId', filters.userId);
    }

    return this.http.get<PagedResultWithSummary<CashSessionListItem, CashSessionSummary>>(this.baseUrl, { params });
  }

  getById(id: number): Observable<CashSession> {
    return this.http.get<CashSession>(`${this.baseUrl}/${id}`);
  }

  open(payload: OpenCashSessionRequest): Observable<CashSession> {
    return this.http.post<CashSession>(`${this.baseUrl}/open`, payload);
  }

  addMovement(id: number, payload: CreateCashMovementRequest): Observable<CashSession> {
    return this.http.post<CashSession>(`${this.baseUrl}/${id}/movements`, payload);
  }

  close(id: number, payload: CloseCashSessionRequest): Observable<CashSession> {
    return this.http.post<CashSession>(`${this.baseUrl}/${id}/close`, payload);
  }
}
