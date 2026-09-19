import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';
import { SaleListItem } from '../../models/sale-list-item.model';
import { SalePaymentMethod, salePaymentMethodLabel } from '../../models/sale-payment-method.model';

@Component({
  selector: 'app-void-sale-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, TextareaModule, ButtonModule],
  templateUrl: './void-sale-dialog.html',
  styleUrl: './void-sale-dialog.scss',
})
export class VoidSaleDialog {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) loading = false;
  @Input() sale: SaleListItem | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirmVoid = new EventEmitter<string>();

  reason = '';

  paymentMethodLabel(): string {
    return this.sale ? salePaymentMethodLabel(this.sale.paymentMethod) : '-';
  }

  cashEffectExplanation(): string {
    if (!this.sale) {
      return '';
    }

    if (this.sale.paymentMethod === SalePaymentMethod.Cash) {
      return 'Si la caja original sigue abierta, sus totales vivos se recalcularán sin una salida adicional. Si ya está cerrada o la venta es histórica sin caja, se requerirá una caja abierta actual y se registrará una salida de efectivo.';
    }

    return `El pago con ${salePaymentMethodLabel(this.sale.paymentMethod).toLowerCase()} no genera un movimiento de efectivo. Si la caja original sigue abierta, se ajustará su total vivo; si ya cerró, el snapshot histórico permanecerá intacto.`;
  }

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (!value) {
      this.reason = '';
    }
  }
}
