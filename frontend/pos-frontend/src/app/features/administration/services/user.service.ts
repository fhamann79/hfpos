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
    return this.http.post<User>(this.baseUrl, payload);
  }

  update(id: number, payload: UpdateUserRequest) {
    return this.http.put<User>(`${this.baseUrl}/${id}`, payload);
  }

  updatePassword(id: number, payload: ChangeUserPasswordRequest) {
    return this.http.put<void>(`${this.baseUrl}/${id}/password`, payload);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
