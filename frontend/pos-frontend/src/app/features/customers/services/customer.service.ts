import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { CreateCustomerRequest, Customer, CustomerStatusFilter, UpdateCustomerRequest } from '../models/customer.model';

@Injectable({ providedIn: 'root' })
export class CustomerService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Customers`;

  getAll(filters: { search?: string; status?: CustomerStatusFilter; take?: number } = {}) {
    let params = new HttpParams();
    const search = filters.search?.trim();

    if (search) {
      params = params.set('search', search);
    }

    if (filters.status) {
      params = params.set('status', filters.status);
    }

    if (filters.take) {
      params = params.set('take', String(filters.take));
    }

    return this.http.get<Customer[]>(this.baseUrl, { params });
  }

  getById(id: number) {
    return this.http.get<Customer>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateCustomerRequest) {
    return this.http.post<Customer>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateCustomerRequest) {
    return this.http.put<void>(`${this.baseUrl}/${id}`, payload);
  }

  deactivate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/deactivate`, {});
  }

  activate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/activate`, {});
  }
}
