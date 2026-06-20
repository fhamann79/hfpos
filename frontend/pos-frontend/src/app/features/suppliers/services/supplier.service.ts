import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { CreateSupplierRequest, Supplier, UpdateSupplierRequest } from '../models/supplier.model';

@Injectable({ providedIn: 'root' })
export class SupplierService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Suppliers`;

  getAll(search?: string) {
    const trimmed = search?.trim();
    const params = trimmed ? new HttpParams().set('search', trimmed) : undefined;

    return this.http.get<Supplier[]>(this.baseUrl, { params });
  }

  getById(id: number) {
    return this.http.get<Supplier>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateSupplierRequest) {
    return this.http.post<Supplier>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateSupplierRequest) {
    return this.http.put<void>(`${this.baseUrl}/${id}`, payload);
  }

  deactivate(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
