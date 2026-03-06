import { Component } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { TabsModule } from 'primeng/tabs';
import { ToastModule } from 'primeng/toast';
import { CategoriesTable } from '../../components/categories/categories-table';
import { ProductsTable } from '../../components/products/products-table';

@Component({
  selector: 'app-catalog-page',
  standalone: true,
  imports: [TabsModule, ToastModule, ConfirmDialogModule, CategoriesTable, ProductsTable],
  providers: [MessageService, ConfirmationService],
  templateUrl: './catalog-page.html',
  styleUrl: './catalog-page.scss',
})
export class CatalogPage {}
