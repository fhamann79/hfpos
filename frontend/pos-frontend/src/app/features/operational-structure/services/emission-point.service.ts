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
    return this.http.put<EmissionPoint>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
