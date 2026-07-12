import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { CreditNoteEligibility } from '../models/credit-note-eligibility.model';

@Injectable({ providedIn: 'root' })
export class CreditNoteService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/CreditNotes`;

  getEligibility(originalSaleId: number): Observable<CreditNoteEligibility> {
    return this.http.get<CreditNoteEligibility>(
      `${this.baseUrl}/original-sales/${originalSaleId}/eligibility`
    );
  }
}
