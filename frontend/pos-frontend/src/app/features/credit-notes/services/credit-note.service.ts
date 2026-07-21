import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  catchError,
  from,
  map,
  mergeMap,
  Observable,
  throwError,
} from 'rxjs';
import { environment } from '../../../../environments/environment';
import { SriSubmissionAttempt } from '../../pos-workstation/models/sri-submission-attempt.model';
import { CreditNoteEligibility } from '../models/credit-note-eligibility.model';
import {
  CancelCreditNoteDraftRequest,
  CreateCreditNoteDraftRequest,
  CreditNote,
  CreditNoteListItem,
} from '../models/credit-note.model';

@Injectable({ providedIn: 'root' })
export class CreditNoteService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/CreditNotes`;

  getEligibility(originalSaleId: number): Observable<CreditNoteEligibility> {
    return this.http.get<CreditNoteEligibility>(
      `${this.baseUrl}/original-sales/${originalSaleId}/eligibility`
    );
  }

  createDraft(payload: CreateCreditNoteDraftRequest): Observable<CreditNote> {
    return this.http.post<CreditNote>(`${this.baseUrl}/drafts`, payload);
  }

  getByOriginalSale(originalSaleId: number): Observable<CreditNoteListItem[]> {
    return this.http.get<CreditNoteListItem[]>(
      `${this.baseUrl}/original-sales/${originalSaleId}`
    );
  }

  getById(creditNoteId: number): Observable<CreditNote> {
    return this.http.get<CreditNote>(`${this.baseUrl}/${creditNoteId}`);
  }

  prepareSriDraft(creditNoteId: number): Observable<CreditNote> {
    return this.http.post<CreditNote>(
      `${this.baseUrl}/${creditNoteId}/sri/prepare-draft`,
      {}
    );
  }

  getSriXmlDraft(creditNoteId: number): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/${creditNoteId}/sri/xml-draft`,
      { responseType: 'blob' }
    );
  }

  signSriXml(creditNoteId: number): Observable<CreditNote> {
    return this.http.post<CreditNote>(
      `${this.baseUrl}/${creditNoteId}/sri/sign`,
      {}
    );
  }

  getSriSignedXml(creditNoteId: number): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/${creditNoteId}/sri/signed-xml`,
      { responseType: 'blob' }
    );
  }

  submitSri(creditNoteId: number): Observable<CreditNote> {
    return this.http.post<CreditNote>(
      `${this.baseUrl}/${creditNoteId}/sri/submit`,
      {}
    );
  }

  checkSriAuthorization(creditNoteId: number): Observable<CreditNote> {
    const url = `${this.baseUrl}/${creditNoteId}/sri/check-authorization`;

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

        return response.body as CreditNote;
      })
    );
  }

  getSriAuthorizedXml(creditNoteId: number): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/${creditNoteId}/sri/authorized-xml`,
      { responseType: 'blob' }
    ).pipe(
      catchError((error) => this.normalizeBlobHttpError(error))
    );
  }

  getSriSubmissionAttempts(
    creditNoteId: number
  ): Observable<SriSubmissionAttempt[]> {
    return this.http.get<SriSubmissionAttempt[]>(
      `${this.baseUrl}/${creditNoteId}/sri/submission-attempts`
    );
  }

  cancelDraft(
    creditNoteId: number,
    payload: CancelCreditNoteDraftRequest
  ): Observable<CreditNote> {
    return this.http.post<CreditNote>(`${this.baseUrl}/${creditNoteId}/cancel`, payload);
  }

  private isApiErrorPayload(value: unknown): boolean {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
      return false;
    }

    const payload = value as Record<string, unknown>;
    return typeof payload['error'] === 'string'
      || typeof payload['code'] === 'string';
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
