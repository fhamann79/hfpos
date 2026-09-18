import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  CreateEmissionPointRequest,
  EmissionPoint,
  UpdateEmissionPointRequest,
} from '../models/emission-point.model';

@Injectable({ providedIn: 'root' })
export class EmissionPointService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/EmissionPoints`;

  getAll(establishmentId: number) {
    const params = new HttpParams().set('establishmentId', establishmentId);
    return this.http.get<EmissionPoint[]>(this.baseUrl, { params });
  }

  create(payload: CreateEmissionPointRequest) {
    return this.http.post<EmissionPoint>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateEmissionPointRequest) {
    const { code, name } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { code, name });
  }

  activate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/activate`, {});
  }

  deactivate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/deactivate`, {});
  }
}
