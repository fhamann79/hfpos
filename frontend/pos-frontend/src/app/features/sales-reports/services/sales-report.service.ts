import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PagedResultWithSummary } from '../../../core/models/paged-result.model';
import {
  SalesReportDetail,
  SalesReportDetailItem,
  SalesReportQuery,
  SalesReportRow,
  SalesReportSummary,
  normalizeSaleDocumentStatus,
  normalizeSaleDocumentType,
  normalizeSaleStatus,
  normalizeVatCategory,
} from '../models/sales-report.model';

@Injectable({ providedIn: 'root' })
export class SalesReportService {
  private readonly http = inject(HttpClient);
  private readonly salesUrl = `${environment.apiUrl}/api/Sales`;

  getSales(query: SalesReportQuery): Observable<PagedResultWithSummary<SalesReportRow, SalesReportSummary>> {
    return this.http.get<unknown>(this.salesUrl, { params: this.buildParams(query, true) }).pipe(
      map((response) => this.toPagedResult(response))
    );
  }

  exportSales(query: SalesReportQuery): Observable<Blob> {
    return this.http.get(`${this.salesUrl}/export`, {
      params: this.buildParams(query, false),
      responseType: 'blob',
    });
  }

  getSaleDetail(id: number): Observable<SalesReportDetail> {
    return this.http.get<unknown>(`${this.salesUrl}/${id}`).pipe(map((row) => this.toSalesReportDetail(row)));
  }

  private buildParams(filters: SalesReportQuery, includePagination: boolean): HttpParams {
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

    if (filters.userId !== null && filters.userId !== undefined) {
      params = params.set('userId', String(filters.userId));
    }

    if (search) {
      params = params.set('search', search);
    }

    params = params
      .set('sortBy', filters.sortBy)
      .set('sortDirection', filters.sortDirection);

    if (includePagination) {
      params = params
        .set('page', filters.page)
        .set('pageSize', filters.pageSize)
        .set('includeSummary', filters.includeSummary);
    }

    return params;
  }

  private toPagedResult(source: unknown): PagedResultWithSummary<SalesReportRow, SalesReportSummary> {
    const result = this.asRecord(source);
    const rawItems = result?.['items'];
    const items = Array.isArray(rawItems) ? rawItems.map((row) => this.toSalesReportRow(row)) : [];

    return {
      items,
      page: this.readNumber(result, ['page'], 1),
      pageSize: this.readNumber(result, ['pageSize'], 50),
      totalItems: this.readNumber(result, ['totalItems'], items.length),
      totalPages: this.readNumber(result, ['totalPages'], items.length > 0 ? 1 : 0),
      summary: this.toSummary(result?.['summary']),
    };
  }

  private toSummary(source: unknown): SalesReportSummary | null {
    const summary = this.asRecord(source);
    if (!summary) {
      return null;
    }

    return {
      salesCount: this.readNumber(summary, ['salesCount'], 0),
      totalSold: this.readNumber(summary, ['totalSold'], 0),
      authorizedCreditNoteTotal: this.readNumber(summary, ['authorizedCreditNoteTotal'], 0),
      authorizedCreditNoteCount: this.readNumber(summary, ['authorizedCreditNoteCount'], 0),
      netTotal: this.readNumber(summary, ['netTotal'], 0),
      netCost: this.readNumber(summary, ['netCost'], 0),
      netGrossProfit: this.readNumber(summary, ['netGrossProfit'], 0),
      netGrossMarginPercent: this.readNumber(summary, ['netGrossMarginPercent'], 0),
      invoiceCount: this.readNumber(summary, ['invoiceCount'], 0),
      ticketCount: this.readNumber(summary, ['ticketCount'], 0),
      voidedCount: this.readNumber(summary, ['voidedCount'], 0),
      authorizedCount: this.readNumber(summary, ['authorizedCount'], 0),
    };
  }

  private toSalesReportRow(source: unknown): SalesReportRow {
    const row = this.asRecord(source);
    const impact = this.asRecord(row?.['creditNoteImpact']);
    const total = this.readNumber(row, ['total', 'grandTotal'], 0);
    const totalCost = this.readNumber(row, ['totalCost'], 0);
    const grossProfit = this.readNumber(row, ['grossProfit'], 0);
    const grossMarginPercent = this.readNumber(row, ['grossMarginPercent'], 0);
    const subtotal = this.readNumber(row, ['subtotal'], totalCost + grossProfit);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      businessDate: this.readString(row, ['businessDate'], null),
      timeZoneIdSnapshot: this.readString(row, ['timeZoneIdSnapshot'], null),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: normalizeSaleStatus(row?.['status']),
      number: this.readString(row, ['number'], null),
      customerName: this.readString(row, ['customerName'], null),
      customerIdentification: this.readString(row, ['customerIdentification'], null),
      customerEmail: this.readString(row, ['customerEmail'], null),
      documentType: normalizeSaleDocumentType(row?.['documentType']),
      documentStatus: normalizeSaleDocumentStatus(row?.['documentStatus']),
      sriAuthorizationStatus: this.readString(row, ['sriAuthorizationStatus'], null),
      total,
      totalCost,
      grossProfit,
      grossMarginPercent,
      creditNoteImpact: {
        authorizedCreditNoteCount: this.readNumber(impact, ['authorizedCreditNoteCount'], 0),
        authorizedCreditNoteTotal: this.readNumber(impact, ['authorizedCreditNoteTotal'], 0),
        authorizedCreditNoteSubtotal: this.readNumber(impact, ['authorizedCreditNoteSubtotal'], 0),
        returnedCost: this.readNumber(impact, ['returnedCost'], 0),
        netTotal: this.readNumber(impact, ['netTotal'], total),
        netSubtotal: this.readNumber(impact, ['netSubtotal'], subtotal),
        netCost: this.readNumber(impact, ['netCost'], totalCost),
        netGrossProfit: this.readNumber(impact, ['netGrossProfit'], grossProfit),
        netGrossMarginPercent: this.readNumber(impact, ['netGrossMarginPercent'], grossMarginPercent),
      },
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
      buyerNameSnapshot: this.readString(row, ['buyerNameSnapshot'], null),
      buyerIdentificationTypeSnapshot: this.readString(row, ['buyerIdentificationTypeSnapshot'], null),
      buyerIdentificationSnapshot: this.readString(row, ['buyerIdentificationSnapshot'], null),
      buyerAddressSnapshot: this.readString(row, ['buyerAddressSnapshot'], null),
      buyerEmailSnapshot: this.readString(row, ['buyerEmailSnapshot'], null),
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
