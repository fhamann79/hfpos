import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { Role } from '../../models/role.model';
import { User } from '../../models/user.model';
import { RoleService } from '../../services/role.service';
import { UserService } from '../../services/user.service';
import { RecoveryDialog } from '../../../../modules/auth/recovery/recovery-dialog';
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
    SelectModule,
    ToolbarModule,
    TagModule,
    MessageModule,
    UserDialog,
    RecoveryDialog,
  ],
  templateUrl: './users-table.html',
  styleUrl: './users-table.scss',
})
export class UsersTable implements OnInit {
  @Input() canWrite = false;
  @Input() canRecover = false;

  private readonly userService = inject(UserService);
  private readonly roleService = inject(RoleService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private loadRequestId = 0;

  readonly users = signal<User[]>([]);
  readonly totalItems = signal(0);
  readonly currentPage = signal(1);
  readonly roles = signal<Role[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly revokingUserId = signal<number | null>(null);

  search = '';
  isActive: boolean | null = null;
  roleId: number | null = null;
  first = 0;
  rows = 30;
  readonly statusOptions = [
    { label: 'Todos', value: null },
    { label: 'Activos', value: true },
    { label: 'Inactivos', value: false },
  ];
  userDialogVisible = false;
  passwordDialogVisible = false;
  selectedUser: User | null = null;

  ngOnInit(): void {
    this.loadUsers();
    this.loadRoles();
  }

  loadUsers(page = this.currentPage(), pageSize = this.rows): void {
    const requestId = ++this.loadRequestId;
    this.loading.set(true);
    this.errorMessage.set('');

    this.userService.getPage({ page, pageSize, search: this.search, isActive: this.isActive, roleId: this.roleId }).subscribe({
      next: (result) => {
        if (requestId !== this.loadRequestId) return;
        if (result.totalPages > 0 && result.page > result.totalPages) {
          this.loadUsers(result.totalPages, result.pageSize);
          return;
        }
        this.users.set(result.items);
        this.totalItems.set(result.totalItems);
        this.rows = result.pageSize;
        this.currentPage.set(result.totalItems === 0 ? 1 : result.page);
        this.first = (this.currentPage() - 1) * this.rows;
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (requestId !== this.loadRequestId) return;
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los usuarios.'));
      },
    });
  }

  onUsersLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    this.loadUsers(Math.floor((event.first ?? this.first) / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.currentPage.set(1);
    this.loadUsers();
  }

  clearFilters(): void {
    this.search = '';
    this.isActive = null;
    this.roleId = null;
    this.applyFilters();
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
    if (!this.canRecover || !user.isActive) {
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
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
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
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
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
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }

  confirmRevokeSessions(user: User): void {
    if (!this.canWrite || this.revokingUserId() !== null) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Cerrar sesiones',
      message: `Los tokens emitidos para "${user.username}" dejarán de ser válidos. Esto no desactiva ni elimina al usuario, ni cambia su contraseña. ¿Continuar?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Cerrar sesiones',
      rejectLabel: 'Cancelar',
      accept: () => {
        if (!this.canWrite || this.revokingUserId() !== null) {
          return;
        }
        this.revokingUserId.set(user.id);
        this.userService.revokeSessions(user.id).subscribe({
          next: () => {
            this.revokingUserId.set(null);
            this.messageService.add({ severity: 'success', summary: 'Sesiones cerradas', detail: 'Los tokens anteriores ya no son válidos.' });
            this.loadUsers();
          },
          error: (error: HttpErrorResponse) => {
            this.revokingUserId.set(null);
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
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

}
