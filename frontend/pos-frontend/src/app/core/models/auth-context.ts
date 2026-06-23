export interface AuthContext {
  userId: string;
  username: string;
  companyId: number;
  companyTimeZoneId: string;
  establishmentId: number;
  emissionPointId: number;
  roleCode: string;
  permissions: string[];
}
