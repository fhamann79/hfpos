import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { Company, CreateCompanyRequest, UpdateCompanyRequest } from '../models/company.model';

@Injectable({ providedIn: 'root' })
export class CompanyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Companies`;

  getAll() {
    return this.http.get<Company[]>(this.baseUrl);
  }

  create(payload: CreateCompanyRequest) {
    return this.http.post<Company>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateCompanyRequest) {
    return this.http.put<Company>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
