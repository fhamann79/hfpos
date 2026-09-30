import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { PlatformMe, PlatformPage, ProvisionRequest, ProvisionResult, Tenant, TenantDetail, TenantEvent } from './platform.model';

@Injectable({ providedIn: 'root' })
export class PlatformApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/platform`;
  login(username: string, password: string) { return this.http.post<{ token: string }>(`${this.base}/auth/login`, { username, password }); }
  me() { return this.http.get<PlatformMe>(`${this.base}/auth/me`); }
  tenants(search: string, status: string | null, page: number, pageSize: number) {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search.trim()) params = params.set('search', search.trim());
    if (status) params = params.set('status', status);
    return this.http.get<PlatformPage<Tenant>>(`${this.base}/tenants`, { params });
  }
  detail(id: number) { return this.http.get<TenantDetail>(`${this.base}/tenants/${id}`); }
  provision(request: ProvisionRequest) { return this.http.post<ProvisionResult>(`${this.base}/tenants`, request); }
  setActive(id: number, active: boolean, reason: string) {
    return this.http.post<TenantDetail>(`${this.base}/tenants/${id}/${active ? 'activate' : 'suspend'}`, { reason });
  }
  events(id: number, page: number, pageSize: number) {
    return this.http.get<PlatformPage<TenantEvent>>(`${this.base}/tenants/${id}/events`, { params: { page, pageSize } });
  }
}
