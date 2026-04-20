import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { Category, CreateCategoryRequest, UpdateCategoryRequest } from '../models/category.model';

@Injectable({ providedIn: 'root' })
export class CategoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Categories`;

  getAll() {
    return this.http.get<Category[]>(this.baseUrl);
  }

  getById(id: number) {
    return this.http.get<Category>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateCategoryRequest) {
    return this.http.post<Category>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateCategoryRequest) {
    return this.http.put<Category>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
