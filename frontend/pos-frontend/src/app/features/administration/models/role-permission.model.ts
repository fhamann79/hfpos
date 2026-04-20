export interface RolePermission {
  permissionId: number;
  code: string;
  description: string;
  assigned: boolean;
}

export interface UpdateRolePermissionsRequest {
  permissionIds: number[];
}
