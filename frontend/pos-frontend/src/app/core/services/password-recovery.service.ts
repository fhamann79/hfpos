import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface RecoveryIssued { id: string; token: string; expiresAt: string; }
export interface RecoveryStatus { id: string; expiresAt: string; consumedAt: string | null; revokedAt: string | null; }
export interface RecoveryAccount { id: number; username: string; isActive: boolean; }
export interface RecoveryIssue { reason: string; deliveryReference: string; identityConfirmed: boolean; currentPassword: string; }

@Injectable({ providedIn: 'root' })
export class PasswordRecoveryApi {
  private readonly http = inject(HttpClient);
  private base(platform: boolean) { return `${environment.apiUrl}/api/${platform ? 'platform/' : ''}account/recovery`; }
  issue(platform: boolean, id: number, request: RecoveryIssue) {
    return this.http.post<RecoveryIssued>(`${this.base(platform)}/users/${id}`, request);
  }
  status(platform: boolean, id: number) { return this.http.get<RecoveryStatus[]>(`${this.base(platform)}/users/${id}`); }
  revoke(platform: boolean, id: string, currentPassword: string) {
    return this.http.post<void>(`${this.base(platform)}/${id}/revoke`, { currentPassword, newPassword: '' });
  }
  accounts() { return this.http.get<RecoveryAccount[]>(`${this.base(true)}/users`); }
  complete(platform: boolean, token: string, newPassword: string) {
    return this.http.post<void>(`${this.base(platform)}/complete`, { token, newPassword });
  }
  change(platform: boolean, currentPassword: string, newPassword: string) {
    return this.http.put<void>(`${this.base(platform)}/password`, { currentPassword, newPassword });
  }
}
