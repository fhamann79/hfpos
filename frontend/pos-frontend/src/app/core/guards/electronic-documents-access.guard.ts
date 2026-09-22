import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ELECTRONIC_DOCUMENTS_ACCESS_REQUIREMENT } from '../constants/feature-access';
import { PermissionService } from '../services/permission.service';

export const electronicDocumentsAccessGuard: CanActivateFn = () => {
  const permissionService = inject(PermissionService);
  const router = inject(Router);

  if (permissionService.canAccess(ELECTRONIC_DOCUMENTS_ACCESS_REQUIREMENT)) {
    return true;
  }

  return router.createUrlTree(['/dashboard'], {
    queryParams: { message: 'electronic-documents-denied' },
  });
};
