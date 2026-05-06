import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { hasHttpBusinessError, resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { CheckoutRequest } from '../models/checkout-request.model';
import { Sale } from '../models/sale.model';
import { SaleItem } from '../models/sale-item.model';
import { SaleListItem } from '../models/sale-list-item.model';
import { VoidSaleRequest } from '../models/void-sale.model';

@Injectable({ providedIn: 'root' })
export class PosWorkstationService {
  private readonly http = inject(HttpClient);
  private readonly salesUrl = `${environment.apiUrl}/api/Sales`;

  getSales(): Observable<SaleListItem[]> {
    return this.http.get<unknown[]>(this.salesUrl).pipe(map((rows) => rows.map((row) => this.toSaleListItem(row))));
  }

  getSaleDetail(id: number): Observable<Sale> {
    return this.http.get<unknown>(`${this.salesUrl}/${id}`).pipe(map((row) => this.toSale(row)));
  }

  createSale(payload: CheckoutRequest): Observable<unknown> {
    return this.http.post<unknown>(this.salesUrl, payload);
  }

  voidSale(id: number, payload: VoidSaleRequest): Observable<unknown> {
    return this.http.post<unknown>(`${this.salesUrl}/${id}/void`, payload);
  }

  isBusinessError(error: HttpErrorResponse, code: string): boolean {
    return hasHttpBusinessError(error, code);
  }

  resolveBusinessError(error: HttpErrorResponse): string {
    return resolveHttpErrorMessage(error, 'No se pudo completar la acción. Intenta nuevamente.');
  }

  private toSaleListItem(source: unknown): SaleListItem {
    const row = this.asRecord(source);
    const status = this.readString(row, ['status', 'state'], 'UNKNOWN');
    const isVoided = this.isVoided(row);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: isVoided ? 'Anulada' : status,
      total: this.readNumber(row, ['total', 'grandTotal'], 0),
      createdBy: this.readString(row, ['createdBy', 'username', 'userName'], null),
      isVoided,
    };
  }

  private toSale(source: unknown): Sale {
    const row = this.asRecord(source);
    const itemsRaw = row?.['items'];
    const items = Array.isArray(itemsRaw) ? itemsRaw.map((item) => this.toSaleItem(item)) : [];
    const status = this.readString(row, ['status', 'state'], 'UNKNOWN');
    const isVoided = this.isVoided(row);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: isVoided ? 'Anulada' : status,
      customerName: this.readString(row, ['customerName'], null),
      notes: this.readString(row, ['notes'], null),
      subtotal: this.readNumber(row, ['subtotal'], 0),
      total: this.readNumber(row, ['total', 'grandTotal'], 0),
      createdBy: this.readString(row, ['createdBy', 'username', 'userName'], null),
      isVoided,
      items,
    };
  }

  private toSaleItem(source: unknown): SaleItem {
    const row = this.asRecord(source);

    return {
      productId: this.readNumber(row, ['productId', 'id'], 0),
      productName: this.readString(row, ['productName', 'name'], 'Producto'),
      quantity: this.readNumber(row, ['quantity'], 0),
      unitPrice: this.readNumber(row, ['unitPrice', 'price'], 0),
      subtotal: this.readNumber(row, ['subtotal', 'lineSubtotal'], 0),
    };
  }

  private asRecord(value: unknown): Record<string, unknown> | null {
    return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
  }

  private readString(record: Record<string, unknown> | null, keys: string[], fallback: string): string;
  private readString(record: Record<string, unknown> | null, keys: string[], fallback: null): string | null;
  private readString(record: Record<string, unknown> | null, keys: string[], fallback: string | null): string | null {
    if (!record) {
      return fallback;
    }

    for (const key of keys) {
      const value = record[key];
      if (typeof value === 'string') {
        return value;
      }
    }

    return fallback;
  }

  private readBoolean(record: Record<string, unknown> | null, keys: string[], fallback: boolean): boolean {
    if (!record) {
      return fallback;
    }

    for (const key of keys) {
      const value = record[key];
      if (typeof value === 'boolean') {
        return value;
      }
    }

    return fallback;
  }

  private isVoidedStatus(status: string): boolean {
    const normalized = status.toUpperCase();
    return normalized.includes('VOID') || normalized.includes('ANUL') || normalized.includes('CANCEL');
  }

  private isVoided(record: Record<string, unknown> | null): boolean {
    if (!record) {
      return false;
    }

    const flag = this.readBoolean(record, ['isVoided', 'voided'], false);
    if (flag) {
      return true;
    }

    const voidedAt = record['voidedAt'];
    if (typeof voidedAt === 'string' && voidedAt.trim().length > 0) {
      return true;
    }

    const status = this.readString(record, ['status', 'state'], '');
    if (status && this.isVoidedStatus(status)) {
      return true;
    }

    const statusCode = this.readNumber(record, ['statusCode', 'stateCode'], -1);
    return statusCode === 3;
  }

  private readNumber(record: Record<string, unknown> | null, keys: string[], fallback: number): number {
    if (!record) {
      return fallback;
    }

    for (const key of keys) {
      const value = record[key];
      if (typeof value === 'number' && Number.isFinite(value)) {
        return value;
      }
      if (typeof value === 'string') {
        const parsed = Number(value);
        if (Number.isFinite(parsed)) {
          return parsed;
        }
      }
    }

    return fallback;
  }
}
