import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { RolePermission } from '../../models/role-permission.model';
import { Role } from '../../models/role.model';
import { RoleService } from '../../services/role.service';

export interface RolePermissionsDialogSubmit {
  roleId: number;
  permissionIds: number[];
}

interface PermissionGroup {
  name: string;
  items: RolePermission[];
}

@Component({
  selector: 'app-role-permissions-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, CheckboxModule, ButtonModule, MessageModule],
  templateUrl: './role-permissions-dialog.html',
  styleUrl: './role-permissions-dialog.scss',
})
export class RolePermissionsDialog implements OnChanges {
  private readonly roleService = inject(RoleService);

  @Input({ required: true }) visible = false;
  @Input() role: Role | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<RolePermissionsDialogSubmit>();

  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly permissions = signal<RolePermission[]>([]);

  readonly groupedPermissions = computed<PermissionGroup[]>(() => {
    const groups = new Map<string, RolePermission[]>();

    for (const permission of this.permissions()) {
      const group = this.resolveGroup(permission.code);
      const bucket = groups.get(group) ?? [];
      bucket.push(permission);
      groups.set(group, bucket);
    }

    return Array.from(groups.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, items]) => ({ name, items: items.sort((a, b) => a.code.localeCompare(b.code)) }));
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible && this.role) {
      this.loadPermissions(this.role.id);
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  togglePermission(permissionId: number, assigned: boolean): void {
    this.permissions.update((items) =>
      items.map((permission) => (permission.permissionId === permissionId ? { ...permission, assigned } : permission))
    );
  }

  save(): void {
    if (!this.role) {
      return;
    }

    const permissionIds = this.permissions()
      .filter((permission) => permission.assigned)
      .map((permission) => permission.permissionId);

    this.submitForm.emit({ roleId: this.role.id, permissionIds });
  }

  private loadPermissions(roleId: number): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.roleService.getPermissions(roleId).subscribe({
      next: (permissions) => {
        this.permissions.set(permissions);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los permisos.'));
      },
    });
  }

  private resolveGroup(code: string): string {
    const value = code.split('_').slice(0, 2).join('_');

    switch (value) {
      case 'AUTH_PROBE':
      case 'CATALOG_CATEGORIES':
      case 'CATALOG_PRODUCTS':
      case 'CATALOG':
      case 'OP_STRUCTURE':
      case 'ADMIN_USERS':
      case 'ADMIN_ROLES':
      case 'POS':
      case 'REPORTS':
        return value;
      default:
        return code.split('_')[0] || 'OTROS';
    }
  }

}
