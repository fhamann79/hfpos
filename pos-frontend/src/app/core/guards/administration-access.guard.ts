import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ADMINISTRATION_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const administrationAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(ADMINISTRATION_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'administration-denied' },
  });
};
