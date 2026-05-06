import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { CreatePosCustomerRequest, PosCustomer } from '../models/pos-customer.model';

@Injectable({ providedIn: 'root' })
export class PosCustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Customers`;

  search(search: string): Observable<PosCustomer[]> {
    const params = search.trim().length > 0 ? new HttpParams().set('search', search.trim()) : undefined;
    return this.http.get<unknown[]>(this.baseUrl, { params }).pipe(map((rows) => rows.map((row) => this.toCustomer(row))));
  }

  create(payload: CreatePosCustomerRequest): Observable<PosCustomer> {
    return this.http.post<unknown>(this.baseUrl, payload).pipe(map((row) => this.toCustomer(row)));
  }

  private toCustomer(source: unknown): PosCustomer {
    const row = this.asRecord(source);

    return {
      id: this.readNumber(row, ['id', 'customerId'], 0),
      name: this.readString(row, ['name', 'customerName'], 'Cliente'),
      identification: this.readString(row, ['identification', 'document', 'ruc', 'ci'], null),
      phone: this.readString(row, ['phone', 'mobile'], null),
      isActive: this.readBoolean(row, ['isActive', 'active'], true),
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
      if (typeof value === 'string' && value.trim().length > 0) {
        return value.trim();
      }
    }

    return fallback;
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
}
