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
import { User } from '../../models/user.model';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { ChangePasswordDialog, ChangePasswordDialogSubmit } from './change-password-dialog';
import { UserDialog, UserDialogSubmit } from './user-dialog';

@Component({
  selector: 'app-users-table',
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
    UserDialog,
    ChangePasswordDialog,
  ],
  templateUrl: './users-table.html',
  styleUrl: './users-table.scss',
})
export class UsersTable implements OnInit {
  @Input() canWrite = false;

  private readonly userService = inject(UserService);
  private readonly roleService = inject(RoleService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly users = signal<User[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  userDialogVisible = false;
  passwordDialogVisible = false;
  selectedUser: User | null = null;

  ngOnInit(): void {
    this.loadUsers();
    this.loadRoles();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.userService.getAll().subscribe({
      next: (users) => {
        this.users.set(users);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveErrorMessage(error, 'No se pudieron cargar los usuarios.'));
      },
    });
  }

  loadRoles(): void {
    this.roleService.getAll().subscribe({
      next: (roles) => this.roles.set(roles),
      error: () => this.roles.set([]),
    });
  }

  openCreateDialog(): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedUser = null;
    this.userDialogVisible = true;
  }

  openEditDialog(user: User): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedUser = user;
    this.userDialogVisible = true;
  }

  openChangePasswordDialog(user: User): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedUser = user;
    this.passwordDialogVisible = true;
  }

  onUserDialogVisibleChange(visible: boolean): void {
    this.userDialogVisible = visible;
    if (!visible) {
      this.selectedUser = null;
    }
  }

  onPasswordDialogVisibleChange(visible: boolean): void {
    this.passwordDialogVisible = visible;
    if (!visible) {
      this.selectedUser = null;
    }
  }

  submitUserDialog(event: UserDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    if (event.mode === 'create') {
      this.userService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Usuario creado.' });
          this.userDialogVisible = false;
          this.loadUsers();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
        },
      });
      return;
    }

    this.userService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Usuario actualizado.' });
        this.userDialogVisible = false;
        this.loadUsers();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
      },
    });
  }

  submitPasswordDialog(event: ChangePasswordDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    this.userService.updatePassword(event.userId, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Contraseña actualizada.' });
        this.passwordDialogVisible = false;
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
      },
    });
  }

  confirmDelete(user: User): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar usuario',
      message: `¿Deseas eliminar o desactivar el usuario "${user.username}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'danger' },
      accept: () => {
        this.userService.delete(user.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Usuario eliminado.' });
            this.loadUsers();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
          },
        });
      },
    });
  }

  getRoleLabel(user: User): string {
    if (user.roleName) {
      return user.roleName;
    }

    return this.roles().find((role) => role.id === user.roleId)?.name ?? 'Sin rol';
  }

  private resolveErrorMessage(error: HttpErrorResponse, fallback = 'No se pudo completar la acción.'): string {
    if (error.status === 403) {
      return 'No tienes permisos para esta acción.';
    }

    return fallback;
  }
}
