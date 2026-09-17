import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { Company, UpdateCompanyRequest } from '../models/company.model';

@Injectable({ providedIn: 'root' })
export class CompanyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Companies`;

  getAll() {
    return this.http.get<Company[]>(this.baseUrl);
  }

  update(id: number, payload: UpdateCompanyRequest) {
    const { name, timeZoneId } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { name, timeZoneId });
  }

}
