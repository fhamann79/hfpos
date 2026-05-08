import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { TaxSummary, calculateLineTotal, getVatCategoryOption } from '../../../../core/utils/vat-category';
import { CartItem } from '../../models/cart-item.model';

@Component({
  selector: 'app-cart-workstation',
  standalone: true,
  imports: [CommonModule, FormsModule, CurrencyPipe, ButtonModule, InputNumberModule, TextareaModule],
  templateUrl: './cart-workstation.html',
  styleUrl: './cart-workstation.scss',
})
export class CartWorkstation {
  @Input({ required: true }) items: CartItem[] = [];
  @Input({ required: true }) subtotal = 0;
  @Input({ required: true }) taxAmount = 0;
  @Input({ required: true }) total = 0;
  @Input() taxSummary: TaxSummary | null = null;
  @Input() notes = '';
  @Input() canCheckout = false;
  @Input() inventoryAvailable = false;
  @Input() activeProductId: number | null = null;

  @Output() updateQuantity = new EventEmitter<{ productId: number; quantity: number }>();
  @Output() updateUnitPrice = new EventEmitter<{ productId: number; unitPrice: number }>();
  @Output() removeItem = new EventEmitter<number>();
  @Output() notesChange = new EventEmitter<string>();
  @Output() selectLine = new EventEmitter<number>();
  @Output() checkout = new EventEmitter<void>();

  get hasItems(): boolean {
    return this.items.length > 0;
  }

  lineSubtotal(item: CartItem): number {
    return item.quantity * item.unitPrice;
  }

  lineTotal(item: CartItem): number {
    return calculateLineTotal(item.quantity, item.unitPrice, item.product.vatCategory);
  }

  vatLabel(item: CartItem): string {
    return getVatCategoryOption(item.product.vatCategory).shortLabel;
  }

  isActive(item: CartItem): boolean {
    return item.productId === this.activeProductId;
  }

  maxQuantity(item: CartItem): number | null {
    if (!this.inventoryAvailable) {
      return null;
    }

    return Math.max(item.stock, 1);
  }
}
