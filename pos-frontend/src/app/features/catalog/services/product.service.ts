import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { CreateProductRequest, Product, UpdateProductRequest } from '../models/product.model';

@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Products`;

  getAll() {
    return this.http.get<Product[]>(this.baseUrl);
  }

  getById(id: number) {
    return this.http.get<Product>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateProductRequest) {
    return this.http.post<Product>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateProductRequest) {
    return this.http.put<Product>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
