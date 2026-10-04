import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { InputNumberModule } from 'primeng/inputnumber';
import {
  SALE_DOCUMENT_TYPE_OPTIONS,
  SaleDocumentType,
  saleDocumentTypeLabel,
} from '../../models/sale-document.model';
import {
  SALE_PAYMENT_METHOD_OPTIONS,
  SalePaymentMethod,
  salePaymentMethodLabel,
} from '../../models/sale-payment-method.model';

@Component({
  selector: 'app-checkout-confirm-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, FormsModule, DialogModule, ButtonModule, SelectModule, InputNumberModule],
  templateUrl: './checkout-confirm-dialog.html',
  styleUrl: './checkout-confirm-dialog.scss',
})
export class CheckoutConfirmDialog {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) grossSubtotal = 0;
  @Input({ required: true }) discountAmount = 0;
  @Input({ required: true }) subtotal = 0;
  @Input({ required: true }) taxAmount = 0;
  @Input({ required: true }) total = 0;
  @Input({ required: true }) itemCount = 0;
  @Input() notes = '';
  @Input() loading = false;
  @Input() cashReceived: number | null = null;
  @Output() cashReceivedChange = new EventEmitter<number | null>();
  readonly SalePaymentMethod = SalePaymentMethod;
  @Input() documentType: SaleDocumentType = SaleDocumentType.Ticket;
  @Input() paymentMethod: SalePaymentMethod = SalePaymentMethod.Cash;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() documentTypeChange = new EventEmitter<SaleDocumentType>();
  @Output() paymentMethodChange = new EventEmitter<SalePaymentMethod>();
  @Output() confirm = new EventEmitter<void>();

  readonly documentTypeOptions = SALE_DOCUMENT_TYPE_OPTIONS;
  readonly paymentMethodOptions = SALE_PAYMENT_METHOD_OPTIONS;
  readonly SaleDocumentType = SaleDocumentType;

  documentTypeLabel(type: SaleDocumentType): string {
    return saleDocumentTypeLabel(type);
  }

  paymentMethodLabel(method: SalePaymentMethod): string {
    return salePaymentMethodLabel(method);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.repeat || this.loading) { event.preventDefault(); return; }
    if (event.key === 'Escape') {
      this.visibleChange.emit(false);
      event.preventDefault();
      return;
    }

    const target = event.target as HTMLElement | null;
    if (event.key === 'Enter' && !target?.closest('p-select, button, textarea') && !this.loading) {
      this.confirm.emit();
      event.preventDefault();
    }
  }
}
