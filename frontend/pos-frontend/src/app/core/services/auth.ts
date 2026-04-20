import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MeResponse } from '../models/me';
import { AuthContext } from '../models/auth-context';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = `${environment.apiUrl}/api/auth`;

  private readonly authContextKey = 'auth_context';

  constructor(private http: HttpClient) {}

  login(username: string, password: string) {
    return this.http.post<{ token: string }>(`${this.api}/login`, { username, password });
  }

  me() {
    return this.http.get<MeResponse>(`${this.api}/me`);
  }

  clearContext() {
    localStorage.removeItem(this.authContextKey);
  }

  saveContext(me: MeResponse) {
    const context: AuthContext = {
      userId: me.userId,
      username: me.username,
      companyId: me.companyId,
      establishmentId: me.establishmentId,
      emissionPointId: me.emissionPointId,
      roleCode: me.roleCode,
      permissions: me.permissions,
    };
    localStorage.setItem(this.authContextKey, JSON.stringify(context));
  }

  getContext(): AuthContext | null {
    const raw = localStorage.getItem(this.authContextKey);

    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthContext;
    } catch {
      this.clearContext();
      return null;
    }
  }
}
