import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { CreatePosCustomerRequest, PosCustomer } from '../../models/pos-customer.model';
import { PosCustomerService } from '../../services/pos-customer.service';

@Component({
  selector: 'app-customer-selector-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, InputTextModule, ButtonModule, MessageModule, SelectModule],
  templateUrl: './customer-selector-dialog.html',
  styleUrl: './customer-selector-dialog.scss',
})
export class CustomerSelectorDialog implements AfterViewInit, OnChanges {
  readonly maxEmailLength = 320;
  readonly maxAddressLength = 300;
  readonly identificationTypeOptions = [
    { label: 'RUC', value: '04' },
    { label: 'Cedula', value: '05' },
    { label: 'Pasaporte', value: '06' },
    { label: 'Consumidor final', value: '07' },
  ];

  private readonly customerService = inject(PosCustomerService);

  @Input({ required: true }) visible = false;
  @Input() selectedCustomer: PosCustomer | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() selectCustomer = new EventEmitter<PosCustomer>();

  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;
  @ViewChild('nameInput') private nameInput?: ElementRef<HTMLInputElement>;

  readonly customers = signal<PosCustomer[]>([]);
  readonly loading = signal(false);
  readonly creating = signal(false);
  readonly errorMessage = signal('');
  readonly createErrorMessage = signal('');

  searchTerm = '';
  highlightedIndex = 0;
  createMode = false;
  name = '';
  identificationType = '';
  identification = '';
  phone = '';
  email = '';
  address = '';

  ngAfterViewInit(): void {
    if (this.visible) {
      this.focusLookup();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.resetDialog();
    }
  }

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (value) {
      this.resetDialog();
    }
  }

  onSearchChange(value: string): void {
    this.searchTerm = value;
    this.highlightedIndex = 0;
    this.loadCustomers();
  }

  onLookupKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.close();
      event.preventDefault();
      return;
    }

    if (event.key === 'ArrowDown') {
      this.highlightedIndex = Math.min(this.highlightedIndex + 1, Math.max(this.customers().length - 1, 0));
      event.preventDefault();
      return;
    }

    if (event.key === 'ArrowUp') {
      this.highlightedIndex = Math.max(this.highlightedIndex - 1, 0);
      event.preventDefault();
      return;
    }

    if (event.key === 'Enter') {
      const customer = this.customers()[this.highlightedIndex];
      if (customer) {
        this.pick(customer);
      } else {
        this.enableCreateMode();
      }
      event.preventDefault();
    }
  }

  onCreateKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.createMode = false;
      this.focusLookup();
      event.preventDefault();
      return;
    }

    if (event.key === 'Enter') {
      this.createCustomer();
      event.preventDefault();
    }
  }

  pick(customer: PosCustomer): void {
    this.selectCustomer.emit(customer);
    this.close();
  }

  enableCreateMode(): void {
    this.createMode = true;
    this.name = this.searchTerm.trim();
    this.createErrorMessage.set('');
    setTimeout(() => this.nameInput?.nativeElement.focus(), 0);
  }

  createCustomer(): void {
    const identificationType = this.normalizeOptional(this.identificationType);
    const identification = this.normalizeOptional(this.identification);
    const payload: CreatePosCustomerRequest = {
      name: this.name.trim(),
      identificationType,
      identification,
      phone: this.normalizeOptional(this.phone),
      email: this.normalizeOptional(this.email),
      address: this.normalizeOptional(this.address),
    };

    if (!payload.name) {
      this.createErrorMessage.set('El nombre es obligatorio.');
      return;
    }

    if (payload.email && (payload.email.length > this.maxEmailLength || !this.isValidEmail(payload.email))) {
      this.createErrorMessage.set('Ingresa un email de cliente válido.');
      return;
    }

    const identificationError = this.validateIdentification(identificationType, identification);
    if (identificationError) {
      this.createErrorMessage.set(identificationError);
      return;
    }

    if (payload.address && payload.address.length > this.maxAddressLength) {
      this.createErrorMessage.set('La direccion del cliente no debe superar 300 caracteres.');
      return;
    }

    this.creating.set(true);
    this.createErrorMessage.set('');

    this.customerService.create(payload).subscribe({
      next: (customer) => {
        this.creating.set(false);
        this.pick(customer);
      },
      error: (error: HttpErrorResponse) => {
        this.creating.set(false);
        this.createErrorMessage.set(resolveHttpErrorMessage(error, 'No se pudo crear el cliente.'));
      },
    });
  }

  close(): void {
    this.visibleChange.emit(false);
  }

  private resetDialog(): void {
    this.searchTerm = '';
    this.highlightedIndex = 0;
    this.createMode = false;
    this.name = '';
    this.identificationType = '';
    this.identification = '';
    this.phone = '';
    this.email = '';
    this.address = '';
    this.errorMessage.set('');
    this.createErrorMessage.set('');
    this.loadCustomers();
    this.focusLookup();
  }

  private loadCustomers(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.customerService.search(this.searchTerm).subscribe({
      next: (customers) => {
        this.customers.set(customers);
        this.highlightedIndex = Math.min(this.highlightedIndex, Math.max(customers.length - 1, 0));
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.customers.set([]);
        this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los clientes.'));
      },
    });
  }

  focusLookup(): void {
    setTimeout(() => this.searchInput?.nativeElement.focus(), 0);
  }

  private normalizeOptional(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private isValidEmail(value: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
  }

  private validateIdentification(identificationType: string | null, identification: string | null): string {
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
}
