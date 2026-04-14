import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { InventoryMovement, InventoryMovementDetail } from '../models/inventory-movement.model';
import { InventoryMovementFilters } from '../models/inventory-filters.model';
import { InventoryStock } from '../models/inventory-stock.model';
import { PagedInventoryMovements } from '../models/paged-inventory-movements.model';

interface InventoryStockDto {
  productId: number;
  productName?: string | null;
  name?: string | null;
  stock?: number | null;
  quantity?: number | null;
  currentStock?: number | null;
  establishmentId?: number | null;
  establishmentName?: string | null;
  updatedAt?: string | null;
}

interface InventoryMovementDto {
  id: number;
  createdAt?: string | null;
  createdOn?: string | null;
  date?: string | null;
  productId?: number | null;
  productName?: string | null;
  name?: string | null;
  movementType?: string | null;
  type?: string | null;
  quantity?: number | null;
  createdBy?: string | null;
  userName?: string | null;
  username?: string | null;
  user?: string | null;
  origin?: string | null;
  source?: string | null;
  referenceType?: string | null;
}

interface InventoryMovementDetailDto extends InventoryMovementDto {
  establishmentId?: number | null;
  establishmentName?: string | null;
  previousStock?: number | null;
  stockBefore?: number | null;
  newStock?: number | null;
  stockAfter?: number | null;
  referenceId?: number | null;
  reason?: string | null;
  notes?: string | null;
}

interface PagedInventoryMovementsDto {
  items?: InventoryMovementDto[] | null;
  data?: InventoryMovementDto[] | null;
  records?: InventoryMovementDto[] | null;
  totalRecords?: number | null;
  totalCount?: number | null;
  total?: number | null;
  page?: number | null;
  pageNumber?: number | null;
  pageSize?: number | null;
}

type InventoryMovementsResponse = InventoryMovementDto[] | PagedInventoryMovementsDto;

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/inventory`;

  getStocks(): Observable<InventoryStock[]> {
    return this.http
      .get<InventoryStockDto[]>(`${this.baseUrl}/stocks`)
      .pipe(map((rows) => rows.map((row) => this.toStock(row))));
  }

  getMovements(filters: InventoryMovementFilters, page: number, pageSize: number): Observable<PagedInventoryMovements> {
    return this.http
      .get<InventoryMovementsResponse>(`${this.baseUrl}/movements`, {
        params: this.buildMovementParams(filters, page, pageSize),
      })
      .pipe(map((response) => this.toPagedMovements(response, page, pageSize)));
  }

  getMovementById(id: number): Observable<InventoryMovementDetail> {
    return this.http
      .get<InventoryMovementDetailDto>(`${this.baseUrl}/movements/${id}`)
      .pipe(map((row) => this.toMovementDetail(row)));
  }

  resolveError(error: HttpErrorResponse, fallback: string): string {
    return resolveHttpErrorMessage(error, fallback);
  }

  private buildMovementParams(filters: InventoryMovementFilters, page: number, pageSize: number): HttpParams {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);

    if (filters.productId !== null) {
      params = params.set('productId', filters.productId);
    }

    if (filters.type) {
      params = params.set('movementType', filters.type);
    }

    if (filters.fromDate) {
      params = params.set('fromDate', filters.fromDate);
    }

    if (filters.toDate) {
      params = params.set('toDate', filters.toDate);
    }

    return params;
  }

  private toStock(row: InventoryStockDto): InventoryStock {
    return {
      productId: row.productId,
      productName: this.readString(row.productName, row.name, `Producto ${row.productId}`),
      currentStock: this.readNumber(row.currentStock, row.stock, row.quantity, 0),
      establishmentId: this.readNullableNumber(row.establishmentId),
      establishmentName: this.readNullableString(row.establishmentName),
      updatedAt: this.readNullableString(row.updatedAt),
    };
  }

  private toPagedMovements(response: InventoryMovementsResponse, page: number, pageSize: number): PagedInventoryMovements {
    if (Array.isArray(response)) {
      return {
        items: response.map((row) => this.toMovement(row)),
        totalRecords: response.length,
        page,
        pageSize,
      };
    }

    const rows = response.items ?? response.data ?? response.records ?? [];

    return {
      items: rows.map((row) => this.toMovement(row)),
      totalRecords: this.readNumber(response.totalRecords, response.totalCount, response.total, rows.length),
      page: this.readNumber(response.page, response.pageNumber, page),
      pageSize: this.readNumber(response.pageSize, pageSize),
    };
  }

  private toMovement(row: InventoryMovementDto): InventoryMovement {
    return {
      id: row.id,
      createdAt: this.readString(row.createdAt, row.createdOn, row.date, ''),
      productId: this.readNumber(row.productId, 0),
      productName: this.readString(row.productName, row.name, `Producto ${this.readNumber(row.productId, 0)}`),
      type: this.readString(row.movementType, row.type, 'UNKNOWN'),
      quantity: this.readNumber(row.quantity, 0),
      user: this.readNullableString(row.createdBy, row.userName, row.username, row.user),
      origin: this.readNullableString(row.origin, row.source, row.referenceType),
    };
  }

  private toMovementDetail(row: InventoryMovementDetailDto): InventoryMovementDetail {
    const movement = this.toMovement(row);

    return {
      ...movement,
      establishmentId: this.readNullableNumber(row.establishmentId),
      establishmentName: this.readNullableString(row.establishmentName),
      previousStock: this.readNullableNumber(row.previousStock, row.stockBefore),
      newStock: this.readNullableNumber(row.newStock, row.stockAfter),
      referenceId: this.readNullableNumber(row.referenceId),
      reason: this.readNullableString(row.reason),
      notes: this.readNullableString(row.notes),
    };
  }

  private readString(...values: Array<string | number | null | undefined>): string {
    for (const value of values) {
      if (typeof value === 'string' && value.trim().length > 0) {
        return value.trim();
      }

      if (typeof value === 'number' && Number.isFinite(value)) {
        return String(value);
      }
    }

    return '';
  }

  private readNullableString(...values: Array<string | null | undefined>): string | null {
    const value = this.readString(...values);
    return value.length > 0 ? value : null;
  }

  private readNumber(...values: Array<number | null | undefined>): number {
    for (const value of values) {
      if (typeof value === 'number' && Number.isFinite(value)) {
        return value;
      }
    }

    return 0;
  }

  private readNullableNumber(...values: Array<number | null | undefined>): number | null {
    for (const value of values) {
      if (typeof value === 'number' && Number.isFinite(value)) {
        return value;
      }
    }

    return null;
  }
}
