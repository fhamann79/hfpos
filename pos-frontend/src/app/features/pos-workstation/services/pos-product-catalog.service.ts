import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { forkJoin, map, Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { PosProduct } from '../models/pos-product.model';

@Injectable({ providedIn: 'root' })
export class PosProductCatalogService {
  private readonly http = inject(HttpClient);
  private readonly productsUrl = `${environment.apiUrl}/api/Products`;
  private readonly inventoryUrl = `${environment.apiUrl}/api/Inventory/stocks`;

  getProductsWithStock(): Observable<PosProduct[]> {
    return forkJoin({
      products: this.http.get<unknown[]>(this.productsUrl),
      stocks: this.http.get<unknown[]>(this.inventoryUrl).pipe(catchError(() => of([]))),
    }).pipe(
      map(({ products, stocks }) => {
        const stockMap = this.buildStockMap(stocks);

        return products
          .map((product) => this.normalizeProduct(product, stockMap))
          .filter((product): product is PosProduct => !!product)
          .filter((product) => product.isActive)
          .sort((a, b) => a.name.localeCompare(b.name));
      })
    );
  }

  private buildStockMap(stocks: unknown[]): Map<number, number> {
    const mapRef = new Map<number, number>();

    for (const item of stocks) {
      const row = this.asRecord(item);
      if (!row) {
        continue;
      }

      const id = this.readNumber(row, ['productId', 'id', 'productID']);
      const stock = this.readNumber(row, ['stock', 'quantity', 'availableStock', 'currentStock'], 0);

      if (id !== null) {
        mapRef.set(id, stock ?? 0);
      }
    }

    return mapRef;
  }

  private normalizeProduct(source: unknown, stockMap: Map<number, number>): PosProduct | null {
    const row = this.asRecord(source);
    if (!row) {
      return null;
    }

    const id = this.readNumber(row, ['id', 'productId', 'productID']);
    const name = this.readString(row, ['name', 'productName']);
    const price = this.readNumber(row, ['price', 'unitPrice'], 0) ?? 0;
    const isActive = this.readBoolean(row, ['isActive', 'active'], true);

    if (id === null || !name) {
      return null;
    }

    return {
      id,
      name,
      price,
      isActive,
      stock: stockMap.get(id) ?? 0,
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
