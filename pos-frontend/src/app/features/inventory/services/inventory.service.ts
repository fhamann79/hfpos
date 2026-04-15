import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { InventoryMovement } from '../models/inventory-movement.model';
import { InventoryMovementFilters, InventoryStockFilters } from '../models/inventory-filters.model';
import { InventoryStock } from '../models/inventory-stock.model';
import { PagedResult } from '../models/paged-result.model';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/inventory`;

  getStocks(search?: string | null, productId?: number | null, onlyPositive = false): Observable<InventoryStock[]> {
    return this.http.get<InventoryStock[]>(`${this.baseUrl}/stocks`, {
      params: this.buildStockParams({ search: search ?? null, productId: productId ?? null, onlyPositive }),
    });
  }

  getMovements(filters: InventoryMovementFilters): Observable<PagedResult<InventoryMovement>> {
    return this.http.get<PagedResult<InventoryMovement>>(`${this.baseUrl}/movements`, {
      params: this.buildMovementParams(filters),
    });
  }

  getMovementById(id: number): Observable<InventoryMovement> {
    return this.http.get<InventoryMovement>(`${this.baseUrl}/movements/${id}`);
  }

  resolveError(error: HttpErrorResponse, fallback: string): string {
    return resolveHttpErrorMessage(error, fallback);
  }

  private buildStockParams(filters: InventoryStockFilters): HttpParams {
    let params = new HttpParams().set('onlyPositive', filters.onlyPositive);

    if (filters.search) {
      params = params.set('search', filters.search);
    }

    if (filters.productId !== null) {
      params = params.set('productId', filters.productId);
    }

    return params;
  }

  private buildMovementParams(filters: InventoryMovementFilters): HttpParams {
    let params = new HttpParams().set('page', filters.page).set('pageSize', filters.pageSize);

    if (filters.productId !== null) {
      params = params.set('productId', filters.productId);
    }

    if (filters.type !== null) {
      params = params.set('type', filters.type);
    }

    if (filters.sourceType !== null) {
      params = params.set('sourceType', filters.sourceType);
    }

    if (filters.sourceId !== null) {
      params = params.set('sourceId', filters.sourceId);
    }

    if (filters.from) {
      params = params.set('from', filters.from);
    }

    if (filters.to) {
      params = params.set('to', filters.to);
    }

    if (filters.userId !== null) {
      params = params.set('userId', filters.userId);
    }

    if (filters.search) {
      params = params.set('search', filters.search);
    }

    return params;
  }
}
