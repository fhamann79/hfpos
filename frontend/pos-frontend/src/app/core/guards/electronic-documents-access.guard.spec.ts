import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { routes } from '../../app.routes';
import {
  ELECTRONIC_DOCUMENTS_ACCESS_REQUIREMENT,
  NAVIGATION_ITEMS,
} from '../constants/feature-access';
import { PERMISSIONS } from '../constants/permissions';
import { PermissionService } from '../services/permission.service';
import { electronicDocumentsAccessGuard } from './electronic-documents-access.guard';

describe('electronicDocumentsAccessGuard', () => {
  it('registers the dedicated route and permission-gated navigation item', () => {
    expect(routes.some((route) => route.path === 'electronic-documents')).toBe(true);
    expect(ELECTRONIC_DOCUMENTS_ACCESS_REQUIREMENT.requiredPermissions).toEqual([
      PERMISSIONS.reportsSalesRead,
    ]);
    expect(NAVIGATION_ITEMS).toContainEqual(expect.objectContaining({
      label: 'Documentos electrónicos',
      route: '/electronic-documents',
      requiredPermissions: [PERMISSIONS.reportsSalesRead],
    }));
  });

  it('allows REPORTS_SALES_READ and redirects users without it', () => {
    const permissionService = { canAccess: vi.fn(() => true) };
    const router = { createUrlTree: vi.fn(() => ({ redirect: true })) };
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionService, useValue: permissionService },
        { provide: Router, useValue: router },
      ],
    });

    const allowed = TestBed.runInInjectionContext(() => electronicDocumentsAccessGuard(
      {} as ActivatedRouteSnapshot,
      {} as RouterStateSnapshot
    ));
    expect(allowed).toBe(true);

    permissionService.canAccess.mockReturnValue(false);
    const denied = TestBed.runInInjectionContext(() => electronicDocumentsAccessGuard(
      {} as ActivatedRouteSnapshot,
      {} as RouterStateSnapshot
    ));
    expect(denied).toEqual({ redirect: true });
    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard'], {
      queryParams: { message: 'electronic-documents-denied' },
    });
  });
});
