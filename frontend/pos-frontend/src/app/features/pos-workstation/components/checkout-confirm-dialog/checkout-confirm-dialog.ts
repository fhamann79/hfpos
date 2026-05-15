import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import {
  SALE_DOCUMENT_TYPE_OPTIONS,
  SaleDocumentType,
  saleDocumentTypeLabel,
} from '../../models/sale-document.model';

@Component({
  selector: 'app-checkout-confirm-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, FormsModule, DialogModule, ButtonModule, SelectModule],
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
  @Input() documentType: SaleDocumentType = SaleDocumentType.Ticket;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() documentTypeChange = new EventEmitter<SaleDocumentType>();
  @Output() confirm = new EventEmitter<void>();

  readonly documentTypeOptions = SALE_DOCUMENT_TYPE_OPTIONS;
  readonly SaleDocumentType = SaleDocumentType;

  documentTypeLabel(type: SaleDocumentType): string {
    return saleDocumentTypeLabel(type);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.visibleChange.emit(false);
      event.preventDefault();
      return;
    }

    if (event.key === 'Enter' && !this.loading) {
      this.confirm.emit();
      event.preventDefault();
    }
  }
}
