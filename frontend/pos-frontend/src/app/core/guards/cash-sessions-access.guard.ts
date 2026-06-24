import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CASH_SESSIONS_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const cashSessionsAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(CASH_SESSIONS_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'cash-sessions-denied' },
  });
};
