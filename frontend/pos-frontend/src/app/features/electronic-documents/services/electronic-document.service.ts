import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, from, map, mergeMap, Observable, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  SaleInvoiceEmailDelivery,
  SendSaleInvoiceEmailRequest,
} from '../../pos-workstation/models/sale-invoice-email.model';
import { SriRide } from '../../pos-workstation/models/sri-ride.model';
import { SriSubmissionAttempt } from '../../pos-workstation/models/sri-submission-attempt.model';
import {
  ElectronicDocumentDetail,
  ElectronicDocumentKind,
  ElectronicDocumentListItem,
  ElectronicDocumentListResult,
  ElectronicDocumentQuery,
  electronicDocumentKey,
} from '../models/electronic-document.model';

type ApiElectronicDocumentListItem = Omit<ElectronicDocumentListItem, 'key'>;
type ApiElectronicDocumentDetail = Omit<ElectronicDocumentDetail, 'key'>;

interface ApiElectronicDocumentListResult
  extends Omit<ElectronicDocumentListResult, 'items'> {
  items: ApiElectronicDocumentListItem[];
}

@Injectable({ providedIn: 'root' })
export class ElectronicDocumentService {
  private readonly http = inject(HttpClient);
  private readonly documentsUrl = `${environment.apiUrl}/api/ElectronicDocuments`;

  getDocuments(query: ElectronicDocumentQuery): Observable<ElectronicDocumentListResult> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize)
      .set('sortField', query.sortField)
      .set('sortOrder', query.sortOrder)
      .set('onlyWithSriError', query.onlyWithSriError);

    if (query.from) {
      params = params.set('from', query.from);
    }
    if (query.to) {
      params = params.set('to', query.to);
    }
    if (query.kind !== null) {
      params = params.set('kind', query.kind);
    }
    if (query.documentStatus !== null) {
      params = params.set('documentStatus', query.documentStatus);
    }
    if (query.search?.trim()) {
      params = params.set('search', query.search.trim());
    }

    return this.http.get<ApiElectronicDocumentListResult>(this.documentsUrl, { params }).pipe(
      map((result) => ({
        ...result,
        items: result.items.map((item) => ({ ...item, key: electronicDocumentKey(item) })),
      }))
    );
  }

  getDetail(kind: ElectronicDocumentKind, id: number): Observable<ElectronicDocumentDetail> {
    return this.http
      .get<ApiElectronicDocumentDetail>(`${this.documentsUrl}/${kind}/${id}`)
      .pipe(map((detail) => ({ ...detail, key: electronicDocumentKey(detail) })));
  }

  prepareSriDraft(kind: ElectronicDocumentKind, id: number): Observable<unknown> {
    if (kind !== ElectronicDocumentKind.CreditNote) {
      return throwError(() => new Error('ELECTRONIC_DOCUMENT_PREPARE_NOT_SUPPORTED'));
    }

    return this.http.post(`${this.documentUrl(kind, id)}/sri/prepare-draft`, {});
  }

  signSri(kind: ElectronicDocumentKind, id: number): Observable<unknown> {
    return this.http.post(`${this.documentUrl(kind, id)}/sri/sign`, {});
  }

  submitSri(kind: ElectronicDocumentKind, id: number): Observable<unknown> {
    return this.http.post(`${this.documentUrl(kind, id)}/sri/submit`, {});
  }

  checkSriAuthorization(kind: ElectronicDocumentKind, id: number): Observable<unknown> {
    const url = `${this.documentUrl(kind, id)}/sri/check-authorization`;

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

        return response.body;
      })
    );
  }

  getSriXmlDraft(kind: ElectronicDocumentKind, id: number): Observable<Blob> {
    return this.getBlob(`${this.documentUrl(kind, id)}/sri/xml-draft`);
  }

  getSriSignedXml(kind: ElectronicDocumentKind, id: number): Observable<Blob> {
    return this.getBlob(`${this.documentUrl(kind, id)}/sri/signed-xml`);
  }

  getSriAuthorizedXml(kind: ElectronicDocumentKind, id: number): Observable<Blob> {
    return this.getBlob(`${this.documentUrl(kind, id)}/sri/authorized-xml`);
  }

  getSriRide(kind: ElectronicDocumentKind, id: number): Observable<SriRide> {
    return this.http.get<SriRide>(`${this.documentUrl(kind, id)}/sri/ride`);
  }

  getSriRidePdf(kind: ElectronicDocumentKind, id: number): Observable<Blob> {
    return this.getBlob(`${this.documentUrl(kind, id)}/sri/ride-pdf`);
  }

  getSriSubmissionAttempts(
    kind: ElectronicDocumentKind,
    id: number
  ): Observable<SriSubmissionAttempt[]> {
    return this.http.get<SriSubmissionAttempt[]>(
      `${this.documentUrl(kind, id)}/sri/submission-attempts`
    );
  }

  getSriEmailDeliveries(
    kind: ElectronicDocumentKind,
    id: number
  ): Observable<SaleInvoiceEmailDelivery[]> {
    return this.http.get<SaleInvoiceEmailDelivery[]>(
      `${this.documentUrl(kind, id)}/sri/email-deliveries`
    );
  }

  sendSriEmail(
    kind: ElectronicDocumentKind,
    id: number,
    payload: SendSaleInvoiceEmailRequest
  ): Observable<unknown> {
    return this.http.post(`${this.documentUrl(kind, id)}/sri/email`, payload);
  }

  private documentUrl(kind: ElectronicDocumentKind, id: number): string {
    const collection = kind === ElectronicDocumentKind.CreditNote ? 'CreditNotes' : 'Sales';
    return `${environment.apiUrl}/api/${collection}/${id}`;
  }

  private getBlob(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' }).pipe(
      catchError((error) => this.normalizeBlobHttpError(error))
    );
  }

  private isApiErrorPayload(value: unknown): boolean {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
      return false;
    }

    const payload = value as Record<string, unknown>;
    return typeof payload['error'] === 'string' || typeof payload['code'] === 'string';
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
