import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { PagedResult } from '../../../core/models/paged-result.model';
import {
  ChangeUserPasswordRequest,
  CreateUserRequest,
  UpdateUserRequest,
  User,
  UserPageQuery,
} from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Users`;

  getPage(query: UserPageQuery = {}) {
    let params = new HttpParams()
      .set('page', query.page ?? 1)
      .set('pageSize', query.pageSize ?? 30);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.isActive != null) params = params.set('isActive', query.isActive);
    if (query.roleId != null) params = params.set('roleId', query.roleId);
    return this.http.get<PagedResult<User>>(this.baseUrl, { params });
  }

  getById(id: number) {
    return this.http.get<User>(`${this.baseUrl}/${id}`);
  }

  create(payload: CreateUserRequest) {
    const { username, email, password, roleId, establishmentId, emissionPointId } = payload;
    return this.http.post<User>(this.baseUrl, { username, email, password, roleId, establishmentId, emissionPointId });
  }

  update(id: number, payload: UpdateUserRequest) {
    const { email, roleId, establishmentId, emissionPointId, isActive } = payload;
    return this.http.put<void>(`${this.baseUrl}/${id}`, { email, roleId, establishmentId, emissionPointId, isActive });
  }

  updatePassword(id: number, payload: ChangeUserPasswordRequest) {
    return this.http.put<void>(`${this.baseUrl}/${id}/password`, payload);
  }

  revokeSessions(id: number) {
    return this.http.post<void>(`${this.baseUrl}/${id}/revoke-sessions`, null);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
