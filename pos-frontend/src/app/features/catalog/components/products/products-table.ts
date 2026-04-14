import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { Category } from '../../models/category.model';
import { Product } from '../../models/product.model';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';
import { ProductDialog, ProductDialogSubmit } from './product-dialog';

@Component({
  selector: 'app-products-table',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CurrencyPipe,
    TableModule,
    ButtonModule,
    InputTextModule,
    ToolbarModule,
    TagModule,
    MessageModule,
    ConfirmDialogModule,
    ProductDialog,
  ],
  templateUrl: './products-table.html',
  styleUrl: './products-table.scss',
})
export class ProductsTable implements OnInit {
  @Input() canWrite = false;

  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly products = signal<Product[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  dialogVisible = false;
  selectedProduct: Product | null = null;

  ngOnInit(): void {
    this.loadCatalogData();
  }

  loadCatalogData(): void {
    this.loadCategories();
    this.loadProducts();
  }

  loadProducts(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.productService.getAll().subscribe({
      next: (products) => {
        this.products.set(products);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los productos.'));
      },
    });
  }

  loadCategories(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories),
      error: () => this.categories.set([]),
    });
  }

  getCategoryName(categoryId: number): string {
    return this.categories().find((category) => category.id === categoryId)?.name ?? 'Sin categoría';
  }

  openCreateDialog(): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedProduct = null;
    this.dialogVisible = true;
  }

  openEditDialog(product: Product): void {
    if (!this.canWrite) {
      return;
    }

    this.selectedProduct = product;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) {
      this.selectedProduct = null;
    }
  }

  submitDialog(event: ProductDialogSubmit): void {
    if (!this.canWrite) {
      return;
    }

    if (event.mode === 'create') {
      this.productService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Creado' });
          this.dialogVisible = false;
          this.loadCatalogData();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
      return;
    }

    this.productService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Actualizado' });
        this.dialogVisible = false;
        this.loadCatalogData();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
      },
    });
  }

  confirmDelete(product: Product): void {
    if (!this.canWrite) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Eliminar producto',
      message: `¿Deseas eliminar el producto "${product.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: {
        severity: 'danger',
      },
      accept: () => {
        this.productService.delete(product.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Eliminado' });
            this.loadCatalogData();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }

}
