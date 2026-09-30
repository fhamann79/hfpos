export interface PlatformMe { id: number; username: string; email: string; role: 'PLATFORM_ADMIN'; }
export interface Tenant { id: number; name: string; ruc: string; timeZoneId: string; isActive: boolean; createdAt: string; }
export interface TenantEvent { id: number; eventType: 'Provisioned' | 'Suspended' | 'Reactivated'; platformUsername: string; reason: string | null; createdAt: string; }
export interface TenantDetail {
  company: Tenant; establishmentCount: number; activeEstablishmentCount: number;
  emissionPointCount: number; activeEmissionPointCount: number; userCount: number; activeUserCount: number; events: TenantEvent[];
}
export interface PlatformPage<T> { items: T[]; page: number; pageSize: number; totalItems: number; totalPages: number; }
export interface TenantDraft {
  company: { name: string; ruc: string; timeZoneId: string };
  initialEstablishment: { name: string; address: string };
  initialEmissionPoint: { name: string };
  initialAdmin: { username: string; email: string; password: string };
}
export interface ProvisionRequest extends TenantDraft { requestId: string; }
export interface ProvisionResult { tenant: TenantDetail; wasAlreadyProcessed: boolean; }
