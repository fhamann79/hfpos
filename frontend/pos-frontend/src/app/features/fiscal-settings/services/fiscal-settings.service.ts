import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  CompanyFiscalSettings,
  CompanySriSettings,
  CreateDocumentSequenceRequest,
  DocumentSequence,
  DocumentSequenceAudit,
  DocumentSequenceFilters,
  UpdateCompanyFiscalSettingsRequest,
  UpdateCompanySriSettingsRequest,
  UpdateDocumentSequenceRequest,
} from '../models/fiscal-settings.model';

@Injectable({ providedIn: 'root' })
export class FiscalSettingsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/FiscalSettings`;

  getCompanySettings() {
    return this.http.get<CompanyFiscalSettings>(`${this.baseUrl}/company`);
  }

  updateCompanySettings(payload: UpdateCompanyFiscalSettingsRequest) {
    return this.http.put<CompanyFiscalSettings>(`${this.baseUrl}/company`, payload);
  }

  getSriSettings() {
    return this.http.get<CompanySriSettings>(`${this.baseUrl}/sri`);
  }

  updateSriSettings(payload: UpdateCompanySriSettingsRequest) {
    return this.http.put<CompanySriSettings>(`${this.baseUrl}/sri`, payload);
  }

  getDocumentSequences(filters: DocumentSequenceFilters = {}) {
    let params = new HttpParams();

    if (filters.establishmentId) {
      params = params.set('establishmentId', filters.establishmentId);
    }

    if (filters.emissionPointId) {
      params = params.set('emissionPointId', filters.emissionPointId);
    }

    if (filters.documentType !== undefined && filters.documentType !== null) {
      params = params.set('documentType', filters.documentType);
    }

    return this.http.get<DocumentSequence[]>(`${this.baseUrl}/document-sequences`, { params });
  }

  createDocumentSequence(payload: CreateDocumentSequenceRequest) {
    return this.http.post<DocumentSequence>(`${this.baseUrl}/document-sequences`, payload);
  }

  updateDocumentSequence(id: number, payload: UpdateDocumentSequenceRequest) {
    return this.http.put<DocumentSequence>(`${this.baseUrl}/document-sequences/${id}`, payload);
  }

  getDocumentSequenceAudits(id: number) {
    return this.http.get<DocumentSequenceAudit[]>(`${this.baseUrl}/document-sequences/${id}/audits`);
  }
}
