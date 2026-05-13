import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { FISCAL_SETTINGS_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const fiscalSettingsAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(FISCAL_SETTINGS_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'fiscal-settings-denied' },
  });
};
