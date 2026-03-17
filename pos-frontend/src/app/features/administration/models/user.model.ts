export interface User {
  id: number;
  username: string;
  email: string;
  isActive: boolean;
  roleId: number;
  roleCode: string;
  roleName: string;
  companyId: number;
  establishmentId: number;
  emissionPointId: number;
}

export interface CreateUserRequest {
  username: string;
  email: string;
  password: string;
  roleId: number;
  companyId: number;
  establishmentId: number;
  emissionPointId: number;
}

export interface UpdateUserRequest {
  username: string;
  email: string;
  roleId: number;
  companyId: number;
  establishmentId: number;
  emissionPointId: number;
  isActive: boolean;
}

export interface ChangeUserPasswordRequest {
  newPassword: string;
}
