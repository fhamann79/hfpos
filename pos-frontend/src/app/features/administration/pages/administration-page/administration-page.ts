import { Component, computed, inject } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageModule } from 'primeng/message';
import { TabsModule } from 'primeng/tabs';
import { ToastModule } from 'primeng/toast';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { RolesTable } from '../../components/roles/roles-table';
import { UsersTable } from '../../components/users/users-table';

@Component({
  selector: 'app-administration-page',
  standalone: true,
  imports: [TabsModule, ToastModule, ConfirmDialogModule, MessageModule, UsersTable, RolesTable],
  providers: [MessageService, ConfirmationService],
  templateUrl: './administration-page.html',
  styleUrl: './administration-page.scss',
})
export class AdministrationPage {
  private readonly permissionService = inject(PermissionService);

  readonly canReadUsers = computed(() => this.permissionService.hasPermission(PERMISSIONS.adminUsersRead));
  readonly canWriteUsers = computed(() => this.permissionService.hasPermission(PERMISSIONS.adminUsersWrite));
  readonly canReadRoles = computed(() => this.permissionService.hasPermission(PERMISSIONS.adminRolesRead));
  readonly canWriteRoles = computed(() => this.permissionService.hasPermission(PERMISSIONS.adminRolesWrite));

  readonly initialTab = computed(() => (this.canReadUsers() ? '0' : '1'));
}
