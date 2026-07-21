import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
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

  cancelDraft(
    creditNoteId: number,
    payload: CancelCreditNoteDraftRequest
  ): Observable<CreditNote> {
    return this.http.post<CreditNote>(`${this.baseUrl}/${creditNoteId}/cancel`, payload);
  }
}
