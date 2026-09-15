import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import {
  SALE_PAYMENT_METHOD_OPTIONS,
  SalePaymentMethod,
  salePaymentMethodLabel,
} from '../../../pos-workstation/models/sale-payment-method.model';
import { CreditNote, RefundCreditNoteRequest } from '../../models/credit-note.model';

@Component({
  selector: 'app-credit-note-refund-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, DialogModule,
    InputTextModule, MessageModule, SelectModule, TextareaModule],
  templateUrl: './credit-note-refund-dialog.html',
  styleUrl: './credit-note-refund-dialog.scss',
})
export class CreditNoteRefundDialog implements OnChanges {
  @Input({ required: true }) visible = false;
  @Input() creditNote: CreditNote | null = null;
  @Input() loading = false;
  @Input() hasOpenCashSession: boolean | null = null;
  @Input() cashSessionLoading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirmRefund = new EventEmitter<RefundCreditNoteRequest>();

  readonly paymentMethods = SALE_PAYMENT_METHOD_OPTIONS;
  readonly paymentMethodLabel = salePaymentMethodLabel;
  readonly form = new FormGroup({
    method: new FormControl(SalePaymentMethod.Cash, {
      nonNullable: true, validators: [Validators.required],
    }),
    reference: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(150)] }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  get isCash(): boolean {
    return this.form.controls.method.value === SalePaymentMethod.Cash;
  }

  get cashBlocked(): boolean {
    return this.isCash && (this.cashSessionLoading || this.hasOpenCashSession === false);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['creditNote'] || (changes['visible']?.currentValue === true
      && changes['visible']?.previousValue !== true)) {
      this.form.reset({
        method: this.creditNote?.originalSalePaymentMethod ?? SalePaymentMethod.Cash,
        reference: '', notes: '',
      });
    }
    if (this.loading) {
      this.form.disable({ emitEvent: false });
    } else {
      this.form.enable({ emitEvent: false });
    }
  }

  requestVisibleChange(visible: boolean): void {
    if (!visible && this.loading) {
      return;
    }
    this.visibleChange.emit(visible);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.loading || !this.creditNote || this.form.invalid || this.cashBlocked) {
      return;
    }
    const { method, reference, notes } = this.form.getRawValue();
    if (!this.paymentMethods.some(option => option.value === method)) {
      return;
    }
    this.confirmRefund.emit({ method, reference: reference.trim() || null, notes: notes.trim() || null });
  }
}
