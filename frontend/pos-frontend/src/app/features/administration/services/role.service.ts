import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { RolePermission, UpdateRolePermissionsRequest } from '../models/role-permission.model';
import { CreateRoleRequest, Role, UpdateRoleRequest } from '../models/role.model';

@Injectable({ providedIn: 'root' })
export class RoleService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Roles`;

  getAll() {
    return this.http.get<Role[]>(this.baseUrl);
  }

  getById(id: number) {
    return this.http.get<Role>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateRoleRequest) {
    return this.http.post<Role>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateRoleRequest) {
    return this.http.put<Role>(`${this.baseUrl}/${id}`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  getPermissions(roleId: number) {
    return this.http.get<RolePermission[]>(`${this.baseUrl}/${roleId}/permissions`);
  }

  updatePermissions(roleId: number, payload: UpdateRolePermissionsRequest) {
    return this.http.put<void>(`${this.baseUrl}/${roleId}/permissions`, payload);
  }
}
