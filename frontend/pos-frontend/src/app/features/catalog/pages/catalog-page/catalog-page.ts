import { Component, computed, inject } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageModule } from 'primeng/message';
import { TabsModule } from 'primeng/tabs';
import { ToastModule } from 'primeng/toast';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { CategoriesTable } from '../../components/categories/categories-table';
import { ProductsTable } from '../../components/products/products-table';

@Component({
  selector: 'app-catalog-page',
  standalone: true,
  imports: [TabsModule, ToastModule, ConfirmDialogModule, MessageModule, CategoriesTable, ProductsTable],
  providers: [MessageService, ConfirmationService],
  templateUrl: './catalog-page.html',
  styleUrl: './catalog-page.scss',
})
export class CatalogPage {
  private readonly permissionService = inject(PermissionService);

  readonly canReadCategories = computed(() => this.permissionService.hasPermission(PERMISSIONS.catalogCategoriesRead));
  readonly canWriteCategories = computed(() => this.permissionService.hasPermission(PERMISSIONS.catalogCategoriesWrite));
  readonly canReadProducts = computed(() => this.permissionService.hasPermission(PERMISSIONS.catalogProductsRead));
  readonly canWriteProducts = computed(() => this.permissionService.hasPermission(PERMISSIONS.catalogProductsWrite));
  readonly initialTab = computed(() => (this.canReadCategories() ? '0' : '1'));
}
