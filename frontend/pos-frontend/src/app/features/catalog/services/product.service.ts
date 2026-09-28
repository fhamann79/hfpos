import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { PagedResult } from '../../../core/models/paged-result.model';
import { environment } from '../../../../environments/environment';
import { CreateProductRequest, Product, UpdateProductRequest } from '../models/product.model';

export type ProductStatusFilter = 'active' | 'inactive' | 'all';

export interface ProductPageQuery {
  search?: string;
  status?: ProductStatusFilter;
  categoryId?: number | null;
  page?: number;
  pageSize?: number;
  sortBy?: 'name' | 'price' | 'cost' | 'isActive';
  sortDir?: 'asc' | 'desc';
}

@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Products`;

  /** Legacy compatibility for POS/purchase flows. New admin consumers must use getPage. */
  getAll() {
    return this.http.get<Product[]>(this.baseUrl);
  }

  lookup(search?: string, take = 30, categoryId?: number | null) {
    let params = new HttpParams().set('take', String(take));
    const term = search?.trim();
    if (term) params = params.set('search', term);
    if (categoryId) params = params.set('categoryId', String(categoryId));
    return this.http.get<Product[]>(`${this.baseUrl}/lookup`, { params });
  }

  getPage(query: ProductPageQuery = {}) {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 30));
    const term = query.search?.trim();

    if (term) params = params.set('search', term);
    if (query.status) params = params.set('status', query.status);
    if (query.categoryId) params = params.set('categoryId', String(query.categoryId));
    if (query.sortBy) params = params.set('sortBy', query.sortBy);
    if (query.sortDir) params = params.set('sortDir', query.sortDir);

    return this.http.get<PagedResult<Product>>(`${this.baseUrl}/page`, { params });
  }

  getById(id: number) {
    return this.http.get<Product>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateProductRequest) {
    return this.http.post<Product>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateProductRequest) {
    const { categoryId, name, barcode, internalCode, price, cost, minimumStock, vatCategory } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { categoryId, name, barcode, internalCode, price, cost, minimumStock, vatCategory });
  }

  activate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/activate`, {});
  }

  deactivate(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/deactivate`, {});
  }
}
