import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth';
import { AuthStore } from '../stores/auth.store';
import { PermissionRequirement } from '../constants/feature-access';

@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly authStore = inject(AuthStore);
  private readonly authService = inject(AuthService);

  hasPermission(permission: string): boolean {
    return this.getPermissions().includes(permission);
  }

  hasAnyPermission(permissions: string[]): boolean {
    const currentPermissions = this.getPermissions();
    return permissions.some((permission) => currentPermissions.includes(permission));
  }

  hasAllPermissions(permissions: string[]): boolean {
    const currentPermissions = this.getPermissions();
    return permissions.every((permission) => currentPermissions.includes(permission));
  }

  canAccess(requirement: PermissionRequirement): boolean {
    if (!requirement.requiredPermissions?.length) {
      return true;
    }

    const mode = requirement.matchMode ?? 'any';

    return mode === 'all'
      ? this.hasAllPermissions(requirement.requiredPermissions)
      : this.hasAnyPermission(requirement.requiredPermissions);
  }

  private getPermissions(): string[] {
    const storePermissions = this.normalizePermissions(this.authStore.permissions());

    if (storePermissions.length > 0) {
      return storePermissions;
    }

    return this.normalizePermissions(this.authService.getContext()?.permissions);
  }

  private normalizePermissions(value: unknown): string[] {
    if (!Array.isArray(value)) {
      return [];
    }

    return Array.from(
      new Set(
        value
          .filter((permission): permission is string => typeof permission === 'string')
          .map((permission) => permission.trim())
          .filter((permission) => permission.length > 0)
      )
    );
  }
}
