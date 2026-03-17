import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthStore } from '../../../core/stores/auth.store';
import { RolePermission, UpdateRolePermissionsRequest } from '../models/role-permission.model';
import { CreateRoleRequest, Role, UpdateRoleRequest } from '../models/role.model';

@Injectable({ providedIn: 'root' })
export class RoleService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly baseUrl = `${environment.apiUrl}/api/Roles`;

  getAll() {
    return this.http.get<Role[]>(this.baseUrl).pipe(catchError((error) => this.handleAuthError(error)));
  }

  getById(id: number) {
    return this.http.get<Role>(`${this.baseUrl}/${id}`).pipe(catchError((error) => this.handleAuthError(error)));
  }

  create(payload: CreateRoleRequest) {
    return this.http.post<Role>(this.baseUrl, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  update(id: number, payload: UpdateRoleRequest) {
    return this.http.put<Role>(`${this.baseUrl}/${id}`, payload).pipe(catchError((error) => this.handleAuthError(error)));
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`).pipe(catchError((error) => this.handleAuthError(error)));
  }

  getPermissions(roleId: number) {
    return this.http
      .get<RolePermission[]>(`${this.baseUrl}/${roleId}/permissions`)
      .pipe(catchError((error) => this.handleAuthError(error)));
  }

  updatePermissions(roleId: number, payload: UpdateRolePermissionsRequest) {
    return this.http
      .put<void>(`${this.baseUrl}/${roleId}/permissions`, payload)
      .pipe(catchError((error) => this.handleAuthError(error)));
  }

  private handleAuthError(error: HttpErrorResponse) {
    if (error.status === 401) {
      this.authStore.clear();
      this.router.navigateByUrl('/login');
    }

    return throwError(() => error);
  }
}
