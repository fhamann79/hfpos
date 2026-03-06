import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '../../../core/stores/auth.store';
import { environment } from '../../../../environments/environment';
import { CreateProductRequest, Product, UpdateProductRequest } from '../models/product.model';

@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly baseUrl = `${environment.apiUrl}/api/Products`;

  getAll() {
    return this.http.get<Product[]>(this.baseUrl).pipe(catchError((error) => this.handleAuthError(error)));
  }

  getById(id: number) {
    return this.http.get<Product>(`${this.baseUrl}/${id}`).pipe(catchError((error) => this.handleAuthError(error)));
  }

  create(payload: CreateProductRequest) {
    return this.http.post<Product>(this.baseUrl, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  update(id: number, payload: UpdateProductRequest) {
    return this.http.put<Product>(`${this.baseUrl}/${id}`, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`).pipe(catchError((error) => this.handleAuthError(error)));
  }

  private handleAuthError(error: HttpErrorResponse) {
    if (error.status === 401) {
      this.authStore.clear();
      this.router.navigateByUrl('/login');
    }

    return throwError(() => error);
  }
}
