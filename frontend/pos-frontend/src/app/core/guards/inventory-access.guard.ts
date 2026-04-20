import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { INVENTORY_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const inventoryAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(INVENTORY_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'inventory-denied' },
  });
};
