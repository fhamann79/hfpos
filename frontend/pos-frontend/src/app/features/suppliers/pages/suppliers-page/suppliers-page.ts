import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { ToolbarModule } from 'primeng/toolbar';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { CreateSupplierRequest, Supplier, UpdateSupplierRequest } from '../../models/supplier.model';
import { SupplierService } from '../../services/supplier.service';

@Component({
  selector: 'app-suppliers-page',
  standalone: true,
  imports: [
    CommonModule,
    DatePipe,
    FormsModule,
    ReactiveFormsModule,
    TableModule,
    ButtonModule,
    CheckboxModule,
    ConfirmDialogModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    TagModule,
    TextareaModule,
    ToastModule,
    ToolbarModule,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './suppliers-page.html',
  styleUrl: './suppliers-page.scss',
})
export class SuppliersPage implements OnInit {
  private readonly supplierService = inject(SupplierService);
  private readonly permissionService = inject(PermissionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly fb = inject(FormBuilder);

  readonly suppliers = signal<Supplier[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly errorMessage = signal('');
  readonly selectedSupplier = signal<Supplier | null>(null);

  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.suppliersWrite));
  readonly activeCount = computed(() => this.suppliers().filter((supplier) => supplier.isActive).length);
  readonly inactiveCount = computed(() => this.suppliers().filter((supplier) => !supplier.isActive).length);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    identification: ['', [Validators.maxLength(20)]],
    email: ['', [Validators.email, Validators.maxLength(320)]],
    phone: ['', [Validators.maxLength(30)]],
    address: ['', [Validators.maxLength(250)]],
    notes: ['', [Validators.maxLength(500)]],
    isActive: [true],
  });

  search = '';
  dialogVisible = false;

  get isEditMode(): boolean {
    return this.selectedSupplier() !== null;
  }

  ngOnInit(): void {
    this.loadSuppliers();
  }

  loadSuppliers(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.supplierService.getAll(this.search).subscribe({
      next: (suppliers) => {
        this.suppliers.set(suppliers);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los proveedores.'));
      },
    });
  }

  clearSearch(): void {
    this.search = '';
    this.loadSuppliers();
  }

  openCreateDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSupplier.set(null);
    this.form.reset({
      name: '',
      identification: '',
      email: '',
      phone: '',
      address: '',
      notes: '',
      isActive: true,
    });
    this.dialogVisible = true;
  }

  openEditDialog(supplier: Supplier): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSupplier.set(supplier);
    this.form.setValue({
      name: supplier.name,
      identification: supplier.identification ?? '',
      email: supplier.email ?? '',
      phone: supplier.phone ?? '',
      address: supplier.address ?? '',
      notes: supplier.notes ?? '',
      isActive: supplier.isActive,
    });
    this.dialogVisible = true;
  }

  closeDialog(): void {
    this.dialogVisible = false;
    this.selectedSupplier.set(null);
    this.saving.set(false);
  }

  save(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const selected = this.selectedSupplier();
    this.saving.set(true);

    if (selected) {
      this.supplierService.update(selected.id, this.buildUpdatePayload()).subscribe({
        next: () => this.handleSaveSuccess('Proveedor actualizado.'),
        error: (error: HttpErrorResponse) => this.handleSaveError(error),
      });
      return;
    }

    this.supplierService.create(this.buildCreatePayload()).subscribe({
      next: () => this.handleSaveSuccess('Proveedor creado.'),
      error: (error: HttpErrorResponse) => this.handleSaveError(error),
    });
  }

  confirmDeactivate(supplier: Supplier): void {
    if (!this.canWrite() || !supplier.isActive) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Desactivar proveedor',
      message: `Deseas desactivar el proveedor "${supplier.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Desactivar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: {
        severity: 'danger',
      },
      accept: () => {
        this.supplierService.deactivate(supplier.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Listo', detail: 'Proveedor desactivado.' });
            this.loadSuppliers();
          },
          error: (error: HttpErrorResponse) => {
            this.messageService.add({
              severity: 'error',
              summary: 'Error',
              detail: resolveHttpErrorMessage(error, 'No se pudo desactivar el proveedor.'),
            });
          },
        });
      },
    });
  }

  private buildCreatePayload(): CreateSupplierRequest {
    const values = this.form.getRawValue();

    return {
      name: values.name.trim(),
      identification: this.normalizeOptionalText(values.identification),
      email: this.normalizeOptionalText(values.email),
      phone: this.normalizeOptionalText(values.phone),
      address: this.normalizeOptionalText(values.address),
      notes: this.normalizeOptionalText(values.notes),
    };
  }

  private buildUpdatePayload(): UpdateSupplierRequest {
    const values = this.form.getRawValue();

    return {
      ...this.buildCreatePayload(),
      isActive: values.isActive,
    };
  }

  private handleSaveSuccess(detail: string): void {
    this.messageService.add({ severity: 'success', summary: 'Listo', detail });
    this.saving.set(false);
    this.closeDialog();
    this.loadSuppliers();
  }

  private handleSaveError(error: HttpErrorResponse): void {
    this.saving.set(false);
    this.messageService.add({
      severity: 'error',
      summary: 'Error',
      detail: resolveHttpErrorMessage(error, 'No se pudo guardar el proveedor.'),
    });
  }

  private normalizeOptionalText(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
}
