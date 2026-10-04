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
    const { name, code, address } = payload;
    return this.http.post<Establishment>(this.baseUrl, { name, code, address });
  }

  update(id: number, payload: UpdateEstablishmentRequest) {
    const { name, code, address } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { name, code, address });
  }

  activate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/activate`, {});
  }

  deactivate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/deactivate`, {});
  }
}
