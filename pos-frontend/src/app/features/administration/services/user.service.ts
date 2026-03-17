import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthStore } from '../../../core/stores/auth.store';
import {
  ChangeUserPasswordRequest,
  CreateUserRequest,
  UpdateUserRequest,
  User,
} from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly baseUrl = `${environment.apiUrl}/api/Users`;

  getAll() {
    return this.http.get<User[]>(this.baseUrl).pipe(catchError((error) => this.handleAuthError(error)));
  }

  getById(id: number) {
    return this.http.get<User>(`${this.baseUrl}/${id}`).pipe(catchError((error) => this.handleAuthError(error)));
  }

  create(payload: CreateUserRequest) {
    return this.http.post<User>(this.baseUrl, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  update(id: number, payload: UpdateUserRequest) {
    return this.http.put<User>(`${this.baseUrl}/${id}`, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  updatePassword(id: number, payload: ChangeUserPasswordRequest) {
    return this.http
      .put<void>(`${this.baseUrl}/${id}/password`, payload)
      .pipe(catchError((error) => this.handleAuthError(error)));
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
