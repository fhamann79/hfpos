import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CATALOG_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const catalogAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(CATALOG_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'catalog-denied' },
  });
};
