import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, from, map, mergeMap, Observable, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { hasHttpBusinessError, resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { normalizeVatCategory } from '../../../core/utils/vat-category';
import { CheckoutRequest } from '../models/checkout-request.model';
import { normalizeSaleDocumentStatus, normalizeSaleDocumentType } from '../models/sale-document.model';
import {
  SendSaleInvoiceEmailRequest,
  SendSaleInvoiceEmailResult,
} from '../models/sale-invoice-email.model';
import { Sale } from '../models/sale.model';
import { SaleItem } from '../models/sale-item.model';
import { SaleListItem } from '../models/sale-list-item.model';
import { SriRide } from '../models/sri-ride.model';
import {
  normalizeSriSubmissionAttemptStatus,
  normalizeSriSubmissionAttemptType,
  SriSubmissionAttempt,
} from '../models/sri-submission-attempt.model';
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

  createSale(payload: CheckoutRequest): Observable<Sale> {
    return this.http.post<unknown>(this.salesUrl, payload).pipe(map((row) => this.toSale(row)));
  }

  voidSale(id: number, payload: VoidSaleRequest): Observable<unknown> {
    return this.http.post<unknown>(`${this.salesUrl}/${id}/void`, payload);
  }

  signInvoiceXml(id: number): Observable<Sale> {
    return this.http.post<unknown>(`${this.salesUrl}/${id}/sri/sign`, {}).pipe(map((row) => this.toSale(row)));
  }

  getSriXmlDraft(id: number): Observable<Blob> {
    return this.http.get(`${this.salesUrl}/${id}/sri/xml-draft`, { responseType: 'blob' });
  }

  getSriSignedXml(id: number): Observable<Blob> {
    return this.http.get(`${this.salesUrl}/${id}/sri/signed-xml`, { responseType: 'blob' });
  }

  getSriAuthorizedXml(id: number): Observable<Blob> {
    return this.http.get(`${this.salesUrl}/${id}/sri/authorized-xml`, { responseType: 'blob' });
  }

  getSriRide(id: number): Observable<SriRide> {
    return this.http.get<SriRide>(`${this.salesUrl}/${id}/sri/ride`);
  }

  getSriRidePdf(id: number): Observable<Blob> {
    return this.http.get(`${this.salesUrl}/${id}/sri/ride-pdf`, { responseType: 'blob' }).pipe(
      catchError((error) => this.normalizeBlobHttpError(error))
    );
  }

  sendSaleInvoiceEmail(id: number, payload: SendSaleInvoiceEmailRequest): Observable<SendSaleInvoiceEmailResult> {
    return this.http.post<SendSaleInvoiceEmailResult>(`${this.salesUrl}/${id}/sri/email`, payload);
  }

  submitSriInvoice(id: number): Observable<Sale> {
    return this.http.post<unknown>(`${this.salesUrl}/${id}/sri/submit`, {}).pipe(map((row) => this.toSale(row)));
  }

  checkSriAuthorization(id: number): Observable<Sale> {
    const url = `${this.salesUrl}/${id}/sri/check-authorization`;

    return this.http.post<unknown>(url, {}, { observe: 'response' }).pipe(
      map((response) => {
        if (response.status === 202 && this.isApiErrorPayload(response.body)) {
          throw new HttpErrorResponse({
            error: response.body,
            status: response.status,
            statusText: response.statusText,
            url,
          });
        }

        return this.toSale(response.body);
      })
    );
  }

  getSriSubmissionAttempts(id: number): Observable<SriSubmissionAttempt[]> {
    return this.http.get<unknown>(`${this.salesUrl}/${id}/sri/submission-attempts`).pipe(
      map((rows) => Array.isArray(rows) ? rows.map((row) => this.toSriSubmissionAttempt(row)) : [])
    );
  }

  isBusinessError(error: HttpErrorResponse, code: string): boolean {
    return hasHttpBusinessError(error, code);
  }

  resolveBusinessError(error: HttpErrorResponse): string {
    return resolveHttpErrorMessage(error, 'No se pudo completar la acción. Intenta nuevamente.');
  }

  private toSaleListItem(source: unknown): SaleListItem {
    const row = this.asRecord(source);
    const status = this.readSaleStatus(row);
    const isVoided = this.isVoided(row);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: isVoided ? 'Anulada' : status,
      documentType: normalizeSaleDocumentType(row?.['documentType']),
      documentStatus: normalizeSaleDocumentStatus(row?.['documentStatus']),
      number: this.readString(row, ['number'], null),
      hasSriXmlDraft: this.readBoolean(row, ['hasSriXmlDraft'], false),
      hasSriSignedXml: this.readBoolean(row, ['hasSriSignedXml'], false),
      sriSignatureStatusKnown: this.hasOwn(row, 'hasSriSignedXml'),
      accessKey: this.readString(row, ['accessKey'], null),
      sriSignedAt: this.readString(row, ['sriSignedAt'], null),
      sriSubmittedAt: this.readString(row, ['sriSubmittedAt'], null),
      sriReceptionStatus: this.readString(row, ['sriReceptionStatus'], null),
      sriAuthorizationStatus: this.readString(row, ['sriAuthorizationStatus'], null),
      sriLastSubmissionError: this.readString(row, ['sriLastSubmissionError'], null),
      sriLastCheckedAt: this.readString(row, ['sriLastCheckedAt'], null),
      total: this.readNumber(row, ['total', 'grandTotal'], 0),
      createdBy: this.readString(row, ['createdBy', 'username', 'userName'], null),
      isVoided,
    };
  }

  private toSale(source: unknown): Sale {
    const row = this.asRecord(source);
    const itemsRaw = row?.['items'];
    const items = Array.isArray(itemsRaw) ? itemsRaw.map((item) => this.toSaleItem(item)) : [];
    const status = this.readSaleStatus(row);
    const isVoided = this.isVoided(row);

    return {
      id: this.readNumber(row, ['id', 'saleId'], 0),
      createdAt: this.readString(row, ['createdAt', 'createdOn', 'date'], ''),
      status: isVoided ? 'Anulada' : status,
      documentType: normalizeSaleDocumentType(row?.['documentType']),
      documentStatus: normalizeSaleDocumentStatus(row?.['documentStatus']),
      number: this.readString(row, ['number'], null),
      establishmentCodeSnapshot: this.readString(row, ['establishmentCodeSnapshot'], null),
      emissionPointCodeSnapshot: this.readString(row, ['emissionPointCodeSnapshot'], null),
      sequential: this.readOptionalNumber(row, ['sequential']),
      documentIssuedAt: this.readString(row, ['documentIssuedAt'], null),
      accessKey: this.readString(row, ['accessKey'], null),
      authorizationNumber: this.readString(row, ['authorizationNumber'], null),
      authorizedAt: this.readString(row, ['authorizedAt'], null),
      sriEnvironment: this.readOptionalNumber(row, ['sriEnvironment']),
      sriEmissionType: this.readOptionalNumber(row, ['sriEmissionType']),
      sriNumericCode: this.readString(row, ['sriNumericCode'], null),
      sriXmlGeneratedAt: this.readString(row, ['sriXmlGeneratedAt'], null),
      hasSriXmlDraft: this.readBoolean(row, ['hasSriXmlDraft'], false),
      sriSignedAt: this.readString(row, ['sriSignedAt'], null),
      hasSriSignedXml: this.readBoolean(row, ['hasSriSignedXml'], false),
      sriSignatureHash: this.readString(row, ['sriSignatureHash'], null),
      sriSigningCertificateThumbprint: this.readString(row, ['sriSigningCertificateThumbprint'], null),
      sriSigningCertificateSubject: this.readString(row, ['sriSigningCertificateSubject'], null),
      sriSigningCertificateSerialNumber: this.readString(row, ['sriSigningCertificateSerialNumber'], null),
      sriSubmittedAt: this.readString(row, ['sriSubmittedAt'], null),
      sriReceptionStatus: this.readString(row, ['sriReceptionStatus'], null),
      sriAuthorizationStatus: this.readString(row, ['sriAuthorizationStatus'], null),
      sriLastSubmissionError: this.readString(row, ['sriLastSubmissionError'], null),
      sriLastCheckedAt: this.readString(row, ['sriLastCheckedAt'], null),
      customerName: this.readString(row, ['customerName'], null),
      customerEmail: this.readString(row, ['customerEmail'], null),
      notes: this.readString(row, ['notes'], null),
      grossSubtotal: this.readNumber(row, ['grossSubtotal', 'subtotal'], 0),
      discountAmount: this.readNumber(row, ['discountAmount'], 0),
      subtotal: this.readNumber(row, ['subtotal'], 0),
      taxAmount: this.readNumber(row, ['taxAmount'], 0),
      vat15Subtotal: this.readNumber(row, ['vat15Subtotal'], 0),
      vat5Subtotal: this.readNumber(row, ['vat5Subtotal'], 0),
      vat0Subtotal: this.readNumber(row, ['vat0Subtotal'], 0),
      vatExemptSubtotal: this.readNumber(row, ['vatExemptSubtotal'], 0),
      vatNotSubjectSubtotal: this.readNumber(row, ['vatNotSubjectSubtotal'], 0),
      total: this.readNumber(row, ['total', 'grandTotal'], 0),
      createdBy: this.readString(row, ['createdBy', 'username', 'userName'], null),
      isVoided,
      items,
    };
  }

  private toSriSubmissionAttempt(source: unknown): SriSubmissionAttempt {
    const row = this.asRecord(source);

    return {
      id: this.readNumber(row, ['id'], 0),
      saleId: this.readNumber(row, ['saleId'], 0),
      accessKey: this.readString(row, ['accessKey'], ''),
      environment: this.readNumber(row, ['environment'], 1),
      attemptType: normalizeSriSubmissionAttemptType(row?.['attemptType']),
      status: normalizeSriSubmissionAttemptStatus(row?.['status']),
      receptionStatus: this.readString(row, ['receptionStatus'], null),
      authorizationStatus: this.readString(row, ['authorizationStatus'], null),
      authorizationNumber: this.readString(row, ['authorizationNumber'], null),
      authorizationDate: this.readString(row, ['authorizationDate'], null),
      errorCode: this.readString(row, ['errorCode'], null),
      errorMessage: this.readString(row, ['errorMessage'], null),
      sriMessageIdentifier: this.readString(row, ['sriMessageIdentifier'], null),
      sriMessageType: this.readString(row, ['sriMessageType'], null),
      sriMessage: this.readString(row, ['sriMessage'], null),
      sriAdditionalInfo: this.readString(row, ['sriAdditionalInfo'], null),
      createdAt: this.readString(row, ['createdAt'], ''),
      createdByUserId: this.readNumber(row, ['createdByUserId'], 0),
    };
  }

  private toSaleItem(source: unknown): SaleItem {
    const row = this.asRecord(source);

    return {
      productId: this.readNumber(row, ['productId', 'id'], 0),
      productName: this.readString(row, ['productName', 'name'], 'Producto'),
      quantity: this.readNumber(row, ['quantity'], 0),
      unitPrice: this.readNumber(row, ['unitPrice', 'price'], 0),
      grossSubtotal: this.readNumber(row, ['grossSubtotal', 'subtotal', 'lineSubtotal'], 0),
      discountAmount: this.readNumber(row, ['discountAmount'], 0),
      netSubtotal: this.readNumber(row, ['netSubtotal', 'taxableSubtotal', 'subtotal', 'lineSubtotal'], 0),
      subtotal: this.readNumber(row, ['subtotal', 'lineSubtotal'], 0),
      vatCategory: normalizeVatCategory(row?.['vatCategory']),
      vatRate: this.readNumber(row, ['vatRate'], 0),
      taxableSubtotal: this.readNumber(row, ['taxableSubtotal', 'subtotal', 'lineSubtotal'], 0),
      taxAmount: this.readNumber(row, ['taxAmount'], 0),
      lineTotal: this.readNumber(row, ['lineTotal', 'subtotal', 'lineSubtotal'], 0),
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

    const statusValue = this.readNumber(record, ['status'], -1);
    if (statusValue === 2) {
      return true;
    }

    const statusCode = this.readNumber(record, ['statusCode', 'stateCode'], -1);
    return statusCode === 3;
  }

  private readSaleStatus(record: Record<string, unknown> | null): string {
    if (!record) {
      return 'Desconocida';
    }

    const status = this.readString(record, ['status', 'state'], '');
    if (status) {
      return status;
    }

    switch (this.readNumber(record, ['status', 'state'], -1)) {
      case 0:
        return 'Borrador';
      case 1:
        return 'Completada';
      case 2:
        return 'Anulada';
      default:
        return 'Desconocida';
    }
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

  private readOptionalNumber(record: Record<string, unknown> | null, keys: string[]): number | null {
    if (!record) {
      return null;
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

    return null;
  }

  private hasOwn(record: Record<string, unknown> | null, key: string): boolean {
    return !!record && Object.prototype.hasOwnProperty.call(record, key);
  }

  private isApiErrorPayload(value: unknown): boolean {
    const record = this.asRecord(value);
    return !!record && (typeof record['error'] === 'string' || typeof record['code'] === 'string');
  }

  private normalizeBlobHttpError(error: unknown): Observable<never> {
    if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
      return throwError(() => error);
    }

    return from(this.readBlobErrorPayload(error.error)).pipe(
      mergeMap((payload) => throwError(() => new HttpErrorResponse({
        error: payload ?? error.error,
        headers: error.headers,
        status: error.status,
        statusText: error.statusText,
        url: error.url ?? undefined,
      })))
    );
  }

  private async readBlobErrorPayload(blob: Blob): Promise<unknown | null> {
    const contentType = blob.type.toLowerCase();
    if (!contentType.includes('json') && !contentType.includes('text')) {
      return null;
    }

    const text = await blob.text();
    if (!text.trim()) {
      return null;
    }

    try {
      return JSON.parse(text) as unknown;
    } catch {
      return text;
    }
  }
}
