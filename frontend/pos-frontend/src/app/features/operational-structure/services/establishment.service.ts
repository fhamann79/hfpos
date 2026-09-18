import { HttpClient } from '@angular/common/http';
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

  getAll() {
    return this.http.get<Establishment[]>(this.baseUrl);
  }

  create(payload: CreateEstablishmentRequest) {
    return this.http.post<Establishment>(this.baseUrl, { name: payload.name });
  }

  update(id: number, payload: UpdateEstablishmentRequest) {
    const { name } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { name });
  }

  activate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/activate`, {});
  }

  deactivate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/deactivate`, {});
  }
}
