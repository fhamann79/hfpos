import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { OPERATIONAL_STRUCTURE_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const operationalStructureAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(OPERATIONAL_STRUCTURE_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'operational-structure-denied' },
  });
};
