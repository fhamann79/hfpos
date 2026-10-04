import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { normalizeVatCategory } from '../../../core/utils/vat-category';
import { PosProduct } from '../models/pos-product.model';

export interface PosCatalogSnapshot {
  products: PosProduct[];
  inventoryAvailable: boolean;
}

@Injectable({ providedIn: 'root' })
export class PosProductCatalogService {
  private readonly http = inject(HttpClient);
  private readonly lookupUrl = `${environment.apiUrl}/api/Inventory/pos-products`;

  /**
   * Compatibility probe for PosWorkstationPage initialization.
   * It verifies that the integrated product+stock lookup is available without
   * materializing a local product catalog.
   */
  getProductsWithStock(): Observable<PosCatalogSnapshot> {
    return this.searchProducts('', 1).pipe(
      map(() => ({ products: [], inventoryAvailable: true })),
      catchError(() => of({ products: [], inventoryAvailable: false }))
    );
  }

  searchProducts(search = '', take = 30): Observable<PosProduct[]> {
    let params = new HttpParams().set('take', String(take));
    const term = search.trim();
    if (term) {
      params = params.set('search', term);
    }

    return this.http.get<unknown[]>(this.lookupUrl, { params }).pipe(
      map((rows) =>
        rows
          .map((row) => this.normalizeProduct(row))
          .filter((product): product is PosProduct => product !== null)
      )
    );
  }

  refreshCartProducts(ids: number[]): Observable<PosProduct[]> {
    const unique = [...new Set(ids)];
    const batches: Observable<PosProduct[]>[] = [];
    for (let offset = 0; offset < unique.length; offset += 100) {
      let params = new HttpParams().set('take', '100');
      for (const id of unique.slice(offset, offset + 100)) params = params.append('productIds', id);
      batches.push(this.http.get<unknown[]>(this.lookupUrl, { params }).pipe(
        map(rows => rows.map(row => this.normalizeProduct(row, true)).filter((p): p is PosProduct => p !== null))));
    }
    return batches.length ? forkJoin(batches).pipe(map(rows => rows.flat())) : of([]);
  }

  private normalizeProduct(source: unknown, includeInactive = false): PosProduct | null {
    const row = this.asRecord(source);
    if (!row) {
      return null;
    }

    const id = this.readNumber(row, ['id', 'productId', 'productID']);
    const name = this.readString(row, ['name', 'productName']);
    const barcode = this.readString(row, ['barcode', 'barCode']);
    const internalCode = this.readString(row, ['internalCode', 'internal_code']);
    const price = this.readNumber(row, ['price', 'unitPrice'], 0) ?? 0;
    const stock = this.readNumber(row, ['stock', 'quantity', 'availableStock', 'currentStock'], 0) ?? 0;
    const vatCategory = normalizeVatCategory(row['vatCategory'] ?? row['VatCategory']);
    const isActive = this.readBoolean(row, ['isActive', 'active'], true);

    if (id === null || !name || (!isActive && !includeInactive)) {
      return null;
    }

    return {
      id,
      name,
      barcode,
      internalCode,
      price,
      vatCategory,
      isActive,
      stock,
    };
  }

  private asRecord(value: unknown): Record<string, unknown> | null {
    return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
  }

  private readString(record: Record<string, unknown>, keys: string[]): string | null {
    for (const key of keys) {
      const value = record[key];
      if (typeof value === 'string' && value.trim().length > 0) {
        return value.trim();
      }
    }

    return null;
  }

  private readBoolean(record: Record<string, unknown>, keys: string[], fallback: boolean): boolean {
    for (const key of keys) {
      const value = record[key];
      if (typeof value === 'boolean') {
        return value;
      }
    }

    return fallback;
  }

  private readNumber(record: Record<string, unknown>, keys: string[], fallback: number | null = null): number | null {
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
