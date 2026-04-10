import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
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
    return this.readErrorCode(error) === code;
  }

  resolveBusinessError(error: HttpErrorResponse): string {
    const code = this.readErrorCode(error);

    if (code === 'INSUFFICIENT_STOCK') {
      return 'Stock insuficiente para completar la venta.';
    }

    if (code === 'SALE_ALREADY_VOIDED') {
      return 'La venta ya fue anulada.';
    }

    if (code === 'PRODUCT_NOT_FOUND') {
      return 'Uno de los productos ya no existe.';
    }

    if (code === 'INVALID_QUANTITY') {
      return 'Hay cantidades inválidas en el carrito.';
    }

    if (error.status === 403) {
      return 'No tienes permisos para realizar esta acción.';
    }

    if (error.status === 0) {
      return 'Error técnico al comunicarse con el backend.';
    }

    return 'No se pudo completar la acción. Intenta nuevamente.';
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

  private readErrorCode(error: HttpErrorResponse): string {
    if (typeof error.error === 'string') {
      return this.detectKnownBusinessCode(error.error);
    }

    const payload = this.asRecord(error.error);
    const directCode = payload?.['code'];

    if (typeof directCode === 'string') {
      return this.detectKnownBusinessCode(directCode);
    }

    const errorCode = payload?.['errorCode'];
    if (typeof errorCode === 'string') {
      return this.detectKnownBusinessCode(errorCode);
    }

    const domainCode = payload?.['domainCode'];
    if (typeof domainCode === 'string') {
      return this.detectKnownBusinessCode(domainCode);
    }

    const errors = payload?.['errors'];
    if (Array.isArray(errors) && errors.length > 0) {
      const firstError = this.asRecord(errors[0]);
      const nestedCode = firstError?.['code'];

      if (typeof nestedCode === 'string') {
        return this.detectKnownBusinessCode(nestedCode);
      }
    }

    const validationErrors = this.asRecord(errors);
    if (validationErrors) {
      const combinedValues = Object.values(validationErrors)
        .filter((value): value is string[] => Array.isArray(value))
        .flat()
        .join(' ');
      const matched = this.detectKnownBusinessCode(combinedValues);
      if (matched) {
        return matched;
      }
    }

    const message = payload?.['message'];
    if (typeof message === 'string') {
      const matched = this.detectKnownBusinessCode(message);
      if (matched) {
        return matched;
      }
    }

    const title = payload?.['title'];
    if (typeof title === 'string') {
      const matched = this.detectKnownBusinessCode(title);
      if (matched) {
        return matched;
      }
    }

    const detail = payload?.['detail'];
    if (typeof detail === 'string') {
      const matched = this.detectKnownBusinessCode(detail);
      if (matched) {
        return matched;
      }
    }

    return '';
  }

  private detectKnownBusinessCode(value: string): string {
    const normalized = this.normalizeCode(value);
    if (normalized.includes('INSUFFICIENT_STOCK') || normalized.includes('INSUFFICIENT STOCK')) {
      return 'INSUFFICIENT_STOCK';
    }
    if (
      normalized.includes('SALE_ALREADY_VOIDED') ||
      normalized.includes('ALREADY VOIDED') ||
      normalized.includes('YA FUE ANULADA')
    ) {
      return 'SALE_ALREADY_VOIDED';
    }

    return normalized;
  }

  private normalizeCode(code: string): string {
    return code.trim().toUpperCase();
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
