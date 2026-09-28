import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { PagedResult } from '../../../core/models/paged-result.model';
import { environment } from '../../../../environments/environment';
import { CreateSupplierRequest, Supplier, UpdateSupplierRequest } from '../models/supplier.model';

export type SupplierStatusFilter = 'active' | 'inactive' | 'all';

export interface SupplierPageQuery {
  search?: string;
  status?: SupplierStatusFilter;
  page?: number;
  pageSize?: number;
  sortBy?: 'name' | 'identification' | 'isActive' | 'updatedAt';
  sortDir?: 'asc' | 'desc';
}

@Injectable({ providedIn: 'root' })
export class SupplierService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Suppliers`;

  getAll(search?: string, take?: number) {
    let params = new HttpParams();
    const trimmed = search?.trim();
    if (trimmed) {
      params = params.set('search', trimmed);
    }
    if (take !== undefined) {
      params = params.set('take', String(take));
    }

    return this.http.get<Supplier[]>(this.baseUrl, { params });
  }

  lookup(search?: string, take = 50) {
    let params = new HttpParams().set('take', String(take));
    const trimmed = search?.trim();
    if (trimmed) {
      params = params.set('search', trimmed);
    }

    return this.http.get<Supplier[]>(`${this.baseUrl}/lookup`, { params });
  }

  getPage(query: SupplierPageQuery = {}) {
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

    return this.http.get<PagedResult<Supplier>>(`${this.baseUrl}/page`, { params });
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
