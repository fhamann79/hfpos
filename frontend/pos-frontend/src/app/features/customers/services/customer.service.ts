import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { PagedResult } from '../../../core/models/paged-result.model';
import { environment } from '../../../../environments/environment';
import { CreateCustomerRequest, Customer, CustomerStatusFilter, UpdateCustomerRequest } from '../models/customer.model';

export interface CustomerPageQuery {
  search?: string;
  status?: CustomerStatusFilter;
  page?: number;
  pageSize?: number;
  sortBy?: 'name' | 'isActive' | 'updatedAt';
  sortDir?: 'asc' | 'desc';
}

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

  getPage(query: CustomerPageQuery = {}) {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 30));
    const search = query.search?.trim();

    if (search) {
      params = params.set('search', search);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }
    if (query.sortDir) {
      params = params.set('sortDir', query.sortDir);
    }

    return this.http.get<PagedResult<Customer>>(`${this.baseUrl}/page`, { params });
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
