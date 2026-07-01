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
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { ToolbarModule } from 'primeng/toolbar';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import {
  CreateCustomerRequest,
  CUSTOMER_IDENTIFICATION_TYPE_OPTIONS,
  CUSTOMER_STATUS_OPTIONS,
  Customer,
  CustomerStatusFilter,
  UpdateCustomerRequest,
  customerIdentificationTypeLabel,
} from '../../models/customer.model';
import { CustomerService } from '../../services/customer.service';

@Component({
  selector: 'app-customers-page',
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
    SelectModule,
    TagModule,
    TextareaModule,
    ToastModule,
    ToolbarModule,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './customers-page.html',
  styleUrl: './customers-page.scss',
})
export class CustomersPage implements OnInit {
  private readonly customerService = inject(CustomerService);
  private readonly permissionService = inject(PermissionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly fb = inject(FormBuilder);

  readonly customers = signal<Customer[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly errorMessage = signal('');
  readonly formError = signal('');
  readonly selectedCustomer = signal<Customer | null>(null);

  readonly identificationTypeOptions = CUSTOMER_IDENTIFICATION_TYPE_OPTIONS;
  readonly statusOptions = CUSTOMER_STATUS_OPTIONS;

  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.customersWrite));
  readonly activeCount = computed(() => this.customers().filter((customer) => customer.isActive).length);
  readonly inactiveCount = computed(() => this.customers().filter((customer) => !customer.isActive).length);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    identificationType: [''],
    identification: ['', [Validators.maxLength(20)]],
    email: ['', [Validators.email, Validators.maxLength(320)]],
    phone: ['', [Validators.maxLength(30)]],
    address: ['', [Validators.maxLength(300)]],
    notes: ['', [Validators.maxLength(500)]],
    isActive: [true],
  });

  search = '';
  status: CustomerStatusFilter = 'active';
  dialogVisible = false;

  get isEditMode(): boolean {
    return this.selectedCustomer() !== null;
  }

  ngOnInit(): void {
    this.loadCustomers();
  }

  loadCustomers(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.customerService.getAll({ search: this.search, status: this.status, take: 200 }).subscribe({
      next: (customers) => {
        this.customers.set(customers);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los clientes.'));
      },
    });
  }

  clearFilters(): void {
    this.search = '';
    this.status = 'active';
    this.loadCustomers();
  }

  openCreateDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedCustomer.set(null);
    this.form.reset({
      name: '',
      identificationType: '',
      identification: '',
      email: '',
      phone: '',
      address: '',
      notes: '',
      isActive: true,
    });
    this.formError.set('');
    this.dialogVisible = true;
  }

  openEditDialog(customer: Customer): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedCustomer.set(customer);
    this.form.setValue({
      name: customer.name,
      identificationType: customer.identificationType ?? '',
      identification: customer.identification ?? '',
      email: customer.email ?? '',
      phone: customer.phone ?? '',
      address: customer.address ?? '',
      notes: customer.notes ?? '',
      isActive: customer.isActive,
    });
    this.formError.set('');
    this.dialogVisible = true;
  }

  closeDialog(): void {
    this.dialogVisible = false;
    this.selectedCustomer.set(null);
    this.saving.set(false);
    this.formError.set('');
  }

  save(): void {
    if (!this.canWrite()) {
      return;
    }

    this.formError.set('');

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const validationMessage = this.validateFiscalFields();
    if (validationMessage) {
      this.formError.set(validationMessage);
      return;
    }

    const selected = this.selectedCustomer();
    this.saving.set(true);

    if (selected) {
      this.customerService.update(selected.id, this.buildUpdatePayload()).subscribe({
        next: () => this.handleSaveSuccess('Cliente actualizado.'),
        error: (error: HttpErrorResponse) => this.handleSaveError(error),
      });
      return;
    }

    this.customerService.create(this.buildCreatePayload()).subscribe({
      next: () => this.handleSaveSuccess('Cliente creado.'),
      error: (error: HttpErrorResponse) => this.handleSaveError(error),
    });
  }

  confirmDeactivate(customer: Customer): void {
    if (!this.canWrite() || !customer.isActive) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Desactivar cliente',
      message: `Deseas desactivar el cliente "${customer.name}"?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Desactivar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: {
        severity: 'danger',
      },
      accept: () => {
        this.customerService.deactivate(customer.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Listo', detail: 'Cliente desactivado.' });
            this.loadCustomers();
          },
          error: (error: HttpErrorResponse) => this.showActionError(error, 'No se pudo desactivar el cliente.'),
        });
      },
    });
  }

  confirmActivate(customer: Customer): void {
    if (!this.canWrite() || customer.isActive) {
      return;
    }

    this.confirmationService.confirm({
      header: 'Reactivar cliente',
      message: `Deseas reactivar el cliente "${customer.name}"?`,
      icon: 'pi pi-check-circle',
      acceptLabel: 'Reactivar',
      rejectLabel: 'Cancelar',
      accept: () => {
        this.customerService.activate(customer.id).subscribe({
          next: () => {
            this.messageService.add({ severity: 'success', summary: 'Listo', detail: 'Cliente reactivado.' });
            this.loadCustomers();
          },
          error: (error: HttpErrorResponse) => this.showActionError(error, 'No se pudo reactivar el cliente.'),
        });
      },
    });
  }

  identificationTypeLabel(value: string | null | undefined): string {
    return customerIdentificationTypeLabel(value);
  }

  private buildCreatePayload(): CreateCustomerRequest {
    const values = this.form.getRawValue();

    return {
      name: values.name.trim(),
      identificationType: this.normalizeOptionalText(values.identificationType),
      identification: this.normalizeOptionalText(values.identification),
      email: this.normalizeOptionalText(values.email),
      phone: this.normalizeOptionalText(values.phone),
      address: this.normalizeOptionalText(values.address),
      notes: this.normalizeOptionalText(values.notes),
    };
  }

  private buildUpdatePayload(): UpdateCustomerRequest {
    const values = this.form.getRawValue();

    return {
      ...this.buildCreatePayload(),
      isActive: values.isActive,
    };
  }

  private validateFiscalFields(): string {
    const values = this.form.getRawValue();
    const identificationType = values.identificationType.trim();
    const identification = values.identification.trim();

    if (identification && !identificationType) {
      return 'Selecciona el tipo de identificacion del cliente.';
    }

    if (identificationType && !identification) {
      return 'Ingresa el numero de identificacion del cliente.';
    }

    if (!identificationType || !identification) {
      return '';
    }

    switch (identificationType) {
      case '04':
        return /^\d{13}$/.test(identification) ? '' : 'El RUC debe tener 13 digitos.';
      case '05':
        return /^\d{10}$/.test(identification) ? '' : 'La cedula debe tener 10 digitos.';
      case '06':
        return /^[A-Za-z0-9]{1,20}$/.test(identification) ? '' : 'El pasaporte debe ser alfanumerico y maximo 20 caracteres.';
      case '07':
        return identification === '9999999999999' ? '' : 'Consumidor final debe usar identificacion 9999999999999.';
      default:
        return 'Selecciona un tipo de identificacion valido.';
    }
  }

  private handleSaveSuccess(detail: string): void {
    this.messageService.add({ severity: 'success', summary: 'Listo', detail });
    this.saving.set(false);
    this.closeDialog();
    this.loadCustomers();
  }

  private handleSaveError(error: HttpErrorResponse): void {
    this.saving.set(false);
    this.messageService.add({
      severity: 'error',
      summary: 'Error',
      detail: resolveHttpErrorMessage(error, 'No se pudo guardar el cliente.'),
    });
  }

  private showActionError(error: HttpErrorResponse, fallback: string): void {
    this.messageService.add({
      severity: 'error',
      summary: 'Error',
      detail: resolveHttpErrorMessage(error, fallback),
    });
  }

  private normalizeOptionalText(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
}
