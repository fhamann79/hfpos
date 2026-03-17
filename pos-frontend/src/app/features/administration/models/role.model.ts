export interface Role {
  id: number;
  code: string;
  name: string;
  isActive: boolean;
  permissionsCount: number;
}

export interface CreateRoleRequest {
  code: string;
  name: string;
}

export interface UpdateRoleRequest {
  name: string;
  isActive: boolean;
}
