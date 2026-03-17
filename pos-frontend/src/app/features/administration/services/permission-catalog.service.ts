import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthStore } from '../../../core/stores/auth.store';
import { Permission } from '../models/permission.model';

@Injectable({ providedIn: 'root' })
export class PermissionCatalogService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly baseUrl = `${environment.apiUrl}/api/Permissions`;

  getAll() {
    return this.http.get<Permission[]>(this.baseUrl).pipe(catchError((error) => this.handleAuthError(error)));
  }

  private handleAuthError(error: HttpErrorResponse) {
    if (error.status === 401) {
      this.authStore.clear();
      this.router.navigateByUrl('/login');
    }

    return throwError(() => error);
  }
}
