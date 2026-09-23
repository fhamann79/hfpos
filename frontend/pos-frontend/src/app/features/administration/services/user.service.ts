import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  ChangeUserPasswordRequest,
  CreateUserRequest,
  UpdateUserRequest,
  User,
} from '../models/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/Users`;

  getAll() {
    return this.http.get<User[]>(this.baseUrl);
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
