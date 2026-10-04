import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { PermissionService } from '../services/permission.service';
export const initialDataAccessGuard: CanActivateFn = () => inject(PermissionService)
  .hasAllPermissions(['OP_STRUCTURE_READ', 'FISCAL_SETTINGS_READ', 'ADMIN_USERS_READ'])
  || inject(Router).createUrlTree(['/dashboard']);
