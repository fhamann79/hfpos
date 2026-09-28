import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import { Category } from '../../models/category.model';
import { Product } from '../../models/product.model';
import { CategoryService } from '../../services/category.service';
import { ProductPageQuery, ProductService, ProductStatusFilter } from '../../services/product.service';
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
    SelectModule,
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
  private loadRequestId = 0;

  readonly products = signal<Product[]>([]);
  readonly categories = signal<Category[]>([]);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly currentPage = signal(1);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly categoryOptions = computed(() => this.categories().map((category) => ({ label: category.name, value: category.id })));
  readonly statusOptions = [
    { label: 'Todos', value: 'all' as ProductStatusFilter },
    { label: 'Activos', value: 'active' as ProductStatusFilter },
    { label: 'Inactivos', value: 'inactive' as ProductStatusFilter },
  ];

  globalFilter = '';
  status: ProductStatusFilter = 'all';
  categoryId: number | null = null;
  first = 0;
  rows = 10;
  sortBy: ProductPageQuery['sortBy'];
  sortDir: ProductPageQuery['sortDir'];
  dialogVisible = false;
  selectedProduct: Product | null = null;

  ngOnInit(): void {
    this.loadCategories();
    this.loadProducts(1, this.rows);
  }

  loadCatalogData(): void {
    this.loadCategories();
    this.loadProducts();
  }

  loadProducts(page = this.currentPage(), pageSize = this.rows): void {
    const requestId = ++this.loadRequestId;
    this.loading.set(true);
    this.errorMessage.set('');

    this.productService
      .getPage({
        search: this.globalFilter,
        status: this.status,
        categoryId: this.categoryId,
        page,
        pageSize,
        sortBy: this.sortBy,
        sortDir: this.sortDir,
      })
      .subscribe({
        next: (result) => {
          if (requestId !== this.loadRequestId) return;

          if (result.totalPages > 0 && result.page > result.totalPages) {
            this.loadProducts(result.totalPages, result.pageSize);
            return;
          }

          this.products.set(result.items);
          this.totalItems.set(result.totalItems);
          this.totalPages.set(result.totalPages);
          this.rows = result.pageSize;
          this.first = result.totalItems === 0 ? 0 : (result.page - 1) * result.pageSize;
          this.currentPage.set(result.totalItems === 0 ? 1 : result.page);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          if (requestId !== this.loadRequestId) return;
          this.loading.set(false);
          this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los productos.'));
        },
      });
  }

  onProductsLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    const first = event.first ?? this.first;
    const sortField = typeof event.sortField === 'string' ? event.sortField : undefined;
    this.sortBy = sortField === 'name' || sortField === 'price' || sortField === 'cost' || sortField === 'isActive'
      ? sortField
      : undefined;
    this.sortDir = event.sortOrder === -1 ? 'desc' : event.sortOrder === 1 ? 'asc' : undefined;
    this.loadProducts(Math.floor(first / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.currentPage.set(1);
    this.loadProducts(1, this.rows);
  }

  clearFilters(): void {
    this.globalFilter = '';
    this.status = 'all';
    this.categoryId = null;
    this.sortBy = undefined;
    this.sortDir = undefined;
    this.applyFilters();
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

  getVatLabel(product: Product): string {
    return getVatCategoryOption(product.vatCategory).shortLabel;
  }

  openCreateDialog(): void {
    if (!this.canWrite) return;
    this.selectedProduct = null;
    this.dialogVisible = true;
  }

  openEditDialog(product: Product): void {
    if (!this.canWrite) return;
    this.selectedProduct = product;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) this.selectedProduct = null;
  }

  submitDialog(event: ProductDialogSubmit): void {
    if (!this.canWrite) return;

    if (event.mode === 'create') {
      this.productService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Creado' });
          this.dialogVisible = false;
          this.loadCategories();
          this.applyFilters();
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

  confirmLifecycle(product: Product): void {
    if (!this.canWrite) return;

    const active = product.isActive;
    const action = active ? 'Desactivar' : 'Activar';
    this.confirmationService.confirm({
      header: `${action} producto`,
      message: `¿Deseas ${action.toLowerCase()} el producto "${product.name}"?`
        + (active ? ' No podrá utilizarse en nuevas ventas o compras. Su historial e inventario se conservarán.' : ''),
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: action,
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: active ? 'warn' : 'success' },
      accept: () => {
        const request = active ? this.productService.deactivate(product.id) : this.productService.activate(product.id);
        request.subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: active ? 'Producto desactivado.' : 'Producto activado.' });
            this.loadProducts();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
          },
        });
      },
    });
  }
}
