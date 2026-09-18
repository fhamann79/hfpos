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
