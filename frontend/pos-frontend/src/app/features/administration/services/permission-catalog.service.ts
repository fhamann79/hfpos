import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { Permission } from '../models/permission.model';

@Injectable({ providedIn: 'root' })
export class PermissionCatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Permissions`;

  getAll() {
    return this.http.get<Permission[]>(this.baseUrl);
  }
}
