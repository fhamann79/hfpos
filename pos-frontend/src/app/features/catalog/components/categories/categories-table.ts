import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService, MessageService } from 'primeng/api';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToolbarModule } from 'primeng/toolbar';
import { Category } from '../../models/category.model';
import { CategoryService } from '../../services/category.service';
import { CategoryDialog, CategoryDialogSubmit } from './category-dialog';

@Component({
  selector: 'app-categories-table',
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
    ConfirmDialogModule,
    CategoryDialog,
  ],
  templateUrl: './categories-table.html',
  styleUrl: './categories-table.scss',
})
export class CategoriesTable implements OnInit {
  private readonly categoryService = inject(CategoryService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');

  globalFilter = '';
  dialogVisible = false;
  selectedCategory: Category | null = null;

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.categoryService.getAll().subscribe({
      next: (categories) => {
        this.categories.set(categories);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveErrorMessage(error, 'No se pudieron cargar las categorías.'));
      },
    });
  }

  openCreateDialog(): void {
    this.selectedCategory = null;
    this.dialogVisible = true;
  }

  openEditDialog(category: Category): void {
    this.selectedCategory = category;
    this.dialogVisible = true;
  }

  onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible = visible;
    if (!visible) {
      this.selectedCategory = null;
    }
  }

  submitDialog(event: CategoryDialogSubmit): void {
    if (event.mode === 'create') {
      this.categoryService.create(event.payload).subscribe({
        next: () => {
          this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Creado' });
          this.dialogVisible = false;
          this.loadCategories();
        },
        error: (error: HttpErrorResponse) => {
          this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
        },
      });
      return;
    }

    this.categoryService.update(event.id, event.payload).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Actualizado' });
        this.dialogVisible = false;
        this.loadCategories();
      },
      error: (error: HttpErrorResponse) => {
        this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
      },
    });
  }

  confirmDelete(category: Category): void {
    this.confirmationService.confirm({
      header: 'Eliminar categoría',
      message: `¿Deseas eliminar la categoría "${category.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Eliminar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: {
        severity: 'danger',
      },
      accept: () => {
        this.categoryService.delete(category.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Éxito', detail: 'Eliminado' });
            this.loadCategories();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({ severity: 'error', summary: 'Error', detail: this.resolveErrorMessage(error) });
          },
        });
      },
    });
  }

  private resolveErrorMessage(error: HttpErrorResponse, fallback = 'No se pudo completar la acción.'): string {
    const backendError = typeof error.error === 'string' ? error.error : error.error?.code;

    if (error.status === 403) {
      return 'No tienes permisos para esta acción';
    }

    if (error.status === 409 && backendError === 'CATEGORY_ALREADY_EXISTS') {
      return 'Ya existe una categoría con ese nombre';
    }

    if (backendError === 'NAME_REQUIRED') {
      return 'El nombre es obligatorio';
    }

    if (backendError === 'CATEGORY_NOT_FOUND') {
      return 'La categoría no existe';
    }

    return fallback;
  }
}
