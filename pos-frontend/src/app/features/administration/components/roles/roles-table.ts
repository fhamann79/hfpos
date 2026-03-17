import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { Role } from '../../models/role.model';
import { RoleService } from '../../services/role.service';
import { RoleDialog, RoleDialogSubmit } from './role-dialog';
import { RolePermissionsDialog, RolePermissionsDialogSubmit } from './role-permissions-dialog';

@Component({
  selector: 'app-roles-table',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    InputTextModule,
    ToolbarModule,
    TagModule,
    MessageModule,
    RoleDialog,
    RolePermissionsDialog,
  ],
  templateUrl: './roles-table.html',
  styleUrl: './roles-table.scss',
})
export class RolesTable implements OnInit {
  @Input() canWrite = false;

  private readonly roleService = inject(RoleService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly roles = signal<Role[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  roleDialogVisible = false;
  permissionsDialogVisible = false;
  selectedRole: Role | null = null;

  ngOnInit(): void {
    this.loadRoles();
  }

  loadRoles(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.roleService.getAll().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveErrorMessage(error, 'No se pudieron cargar los roles.'));
      },
    });
  }

  openCreateDialog(): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedRole = null;
    this.roleDialogVisible = true;
  }

  openEditDialog(role: Role): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedRole = role;
    this.roleDialogVisible = true;
  }

  openPermissionsDialog(role: Role): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedRole = role;
    this.permissionsDialogVisible = true;
  }

  onRoleDialogVisibleChange(visible: boolean): void {
    this.roleDialogVisible = visible;
    if (!visible) {
      this.selectedRole = null;
    }
  }

  onPermissionsDialogVisibleChange(visible: boolean): void {
    this.permissionsDialogVisible = visible;
    if (!visible) {
      this.selectedRole = null;
    }
  }

  submitRoleDialog(event: RoleDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    if (event.mode === 'create') {
      this.roleService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Rol creado.' });
          this.roleDialogVisible = false;
          this.loadRoles();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
        },
      });
      return;
    }

    this.roleService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Rol actualizado.' });
        this.roleDialogVisible = false;
        this.loadRoles();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
      },
    });
  }

  submitPermissionsDialog(event: RolePermissionsDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    this.roleService.updatePermissions(event.roleId, { permissionIds: event.permissionIds }).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Permisos actualizados.' });
        this.permissionsDialogVisible = false;
        this.loadRoles();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
      },
    });
  }

  confirmDelete(role: Role): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar rol',
      message: `¿Deseas eliminar el rol "${role.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => {
        this.roleService.delete(role.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Rol eliminado.' });
            this.loadRoles();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
          },
        });
      },
    });
  }

  private resolveErrorMessage(error: HttpErrorResponse, fallback = 'No se pudo completar la acción.'): string {
    if (error.status === 403) {
      return 'No tienes permisos para esta acción.';
    }

    return fallback;
  }
}
