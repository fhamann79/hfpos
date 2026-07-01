import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CUSTOMERS_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const customersAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(CUSTOMERS_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'customers-denied' },
  });
};
