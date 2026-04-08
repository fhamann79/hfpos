import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
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
  @Input({ required: true }) total = 0;
  @Input() notes = '';
  @Input() canCheckout = false;

  @Output() updateQuantity = new EventEmitter<{ productId: number; quantity: number }>();
  @Output() updateUnitPrice = new EventEmitter<{ productId: number; unitPrice: number }>();
  @Output() removeItem = new EventEmitter<number>();
  @Output() notesChange = new EventEmitter<string>();
  @Output() checkout = new EventEmitter<void>();

  get hasItems(): boolean {
    return this.items.length > 0;
  }

  lineSubtotal(item: CartItem): number {
    return item.quantity * item.unitPrice;
  }
}
