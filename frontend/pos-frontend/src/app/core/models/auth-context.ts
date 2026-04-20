export interface AuthContext {
  userId: string;
  username: string;
  companyId: number;
  establishmentId: number;
  emissionPointId: number;
  roleCode: string;
  permissions: string[];
}
