import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  SalesReportDetail,
  SalesReportDetailItem,
  SalesReportFilters,
  SalesReportRow,
  normalizeSaleDocumentStatus,
  normalizeSaleDocumentType,
  normalizeSaleStatus,
  normalizeVatCategory,
} from '../models/sales-report.model';

@Injectable({ providedIn: 'root' })
export class SalesReportService {
  private readonly http = inject(HttpClient);
  private readonly salesUrl = `${environment.apiUrl}/api/Sales`;

  getSales(filters: SalesReportFilters): Observable<SalesReportRow[]> {
    return this.http.get<unknown[]>(this.salesUrl, { params: this.buildParams(filters) }).pipe(
      map((rows) => rows.map((row) => this.toSalesReportRow(row)))
    );
  }

  getSaleDetail(id: number): Observable<SalesReportDetail> {
    return this.http.get<unknown>(`${this.salesUrl}/${id}`).pipe(map((row) => this.toSalesReportDetail(row)));
  }

  private buildParams(filters: SalesReportFilters): HttpParams {
    let params = new HttpParams();
    const search = filters.search?.trim();

    if (filters.from) {
      params = params.set('from', filters.from);
    }

    if (filters.to) {
      params = params.set('to', filters.to);
    }

    if (filters.status !== null && filters.status !== undefined) {
      params = params.set('status', String(filters.status));
    }

    if (filters.documentType !== null && filters.documentType !== undefined) {
      params = params.set('documentType', String(filters.documentType));
    }

    if (filters.documentStatus !== null && filters.documentStatus !== undefined) {
      params = params.set('documentStatus', String(filters.documentStatus));
    }

    if (search) {
      params = params.set('search', search);
    }

    return params;
  }

  private toSalesReportRow(source: unknown): SalesReportRow {
    const row = this.asRecord(source);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      businessDate: this.readString(row, ['businessDate'], null),
      timeZoneIdSnapshot: this.readString(row, ['timeZoneIdSnapshot'], null),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: normalizeSaleStatus(row?.['status']),
      number: this.readString(row, ['number'], null),
      customerName: this.readString(row, ['customerName'], null),
      customerIdentification: this.readString(row, ['customerIdentification'], null),
      documentType: normalizeSaleDocumentType(row?.['documentType']),
      documentStatus: normalizeSaleDocumentStatus(row?.['documentStatus']),
      sriAuthorizationStatus: this.readString(row, ['sriAuthorizationStatus'], null),
      total: this.readNumber(row, ['total', 'grandTotal'], 0),
      totalCost: this.readNumber(row, ['totalCost'], 0),
      grossProfit: this.readNumber(row, ['grossProfit'], 0),
      grossMarginPercent: this.readNumber(row, ['grossMarginPercent'], 0),
      itemsCount: this.readNumber(row, ['itemsCount'], 0),
      userId: this.readNumber(row, ['userId'], 0),
      username: this.readString(row, ['username', 'createdBy', 'userName'], null),
      notes: this.readString(row, ['notes'], null),
    };
  }

  private toSalesReportDetail(source: unknown): SalesReportDetail {
    const row = this.asRecord(source);
    const itemsRaw = row?.['items'];
    const items = Array.isArray(itemsRaw) ? itemsRaw.map((item) => this.toSalesReportDetailItem(item)) : [];
    const base = this.toSalesReportRow(source);

    return {
      ...base,
      customerEmail: this.readString(row, ['customerEmail'], null),
      paymentMethod: this.readNumber(row, ['paymentMethod'], 0),
      accessKey: this.readString(row, ['accessKey'], null),
      authorizationNumber: this.readString(row, ['authorizationNumber'], null),
      authorizedAt: this.readString(row, ['authorizedAt'], null),
      grossSubtotal: this.readNumber(row, ['grossSubtotal'], 0),
      discountAmount: this.readNumber(row, ['discountAmount'], 0),
      subtotal: this.readNumber(row, ['subtotal'], 0),
      taxAmount: this.readNumber(row, ['taxAmount'], 0),
      items,
    };
  }

  private toSalesReportDetailItem(source: unknown): SalesReportDetailItem {
    const row = this.asRecord(source);

    return {
      id: this.readNumber(row, ['id'], 0),
      productId: this.readNumber(row, ['productId'], 0),
      productName: this.readString(row, ['productName', 'name'], 'Producto'),
      quantity: this.readNumber(row, ['quantity'], 0),
      unitPrice: this.readNumber(row, ['unitPrice', 'price'], 0),
      discountAmount: this.readNumber(row, ['discountAmount'], 0),
      taxableSubtotal: this.readNumber(row, ['taxableSubtotal', 'subtotal', 'lineSubtotal'], 0),
      taxAmount: this.readNumber(row, ['taxAmount'], 0),
      lineTotal: this.readNumber(row, ['lineTotal', 'subtotal', 'lineSubtotal'], 0),
      unitCost: this.readNumber(row, ['unitCost'], 0),
      lineCost: this.readNumber(row, ['lineCost'], 0),
      grossProfit: this.readNumber(row, ['grossProfit'], 0),
      grossMarginPercent: this.readNumber(row, ['grossMarginPercent'], 0),
      vatCategory: normalizeVatCategory(row?.['vatCategory']),
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
