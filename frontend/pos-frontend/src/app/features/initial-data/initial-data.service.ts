import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { InitialDataKind, InitialDataPayload, InitialDataPreview, InitialDataResult, TenantReadiness } from './initial-data.model';

@Injectable({ providedIn: 'root' })
export class InitialDataService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/initial-data`;
  readiness() { return this.http.get<TenantReadiness>(`${this.url}/readiness`); }
  batches(page: number) { return this.http.get<InitialDataResult[]>(`${this.url}/batches`, { params: { page } }); }
  template(kind: InitialDataKind) { return this.http.get(`${this.url}/templates/${kind}`, { responseType: 'blob' }); }
  preview(payload: InitialDataPayload) { return this.http.post<InitialDataPreview>(`${this.url}/preview`, payload); }
  confirm(payload: InitialDataPayload, previewToken: string) {
    return this.http.post<InitialDataResult>(`${this.url}/confirm`, { payload, previewToken });
  }
}
