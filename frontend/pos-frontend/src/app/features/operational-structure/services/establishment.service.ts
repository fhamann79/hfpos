import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  CreateEstablishmentRequest,
  Establishment,
  UpdateEstablishmentRequest,
} from '../models/establishment.model';

@Injectable({ providedIn: 'root' })
export class EstablishmentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Establishments`;

  getAll(companyId: number) {
    const params = new HttpParams().set('companyId', companyId);
    return this.http.get<Establishment[]>(this.baseUrl, { params });
  }

  create(payload: CreateEstablishmentRequest) {
    return this.http.post<Establishment>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateEstablishmentRequest) {
    return this.http.put<Establishment>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
