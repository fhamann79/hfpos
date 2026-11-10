import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, ElementRef, EventEmitter, Input, Output, ViewChild, afterRenderEffect, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { TaxSummary, calculateLineNetSubtotal, getVatCategoryOption } from '../../../../core/utils/vat-category';
import { CartItem } from '../../models/cart-item.model';
import { SalePreview } from '../../models/checkout-request.model';

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
  @Input() fiscalPreview: SalePreview | null = null;
  @Input() saleDiscountAmount = 0;
  @Input() notes = '';
  @Input() canCheckout = false;
  @Input() inventoryAvailable = false;
  @Input() activeProductId: number | null = null;
  readonly revealRequest = input<{ productId: number } | null>(null);

  @ViewChild('cartList') private cartList?: ElementRef<HTMLDivElement>;

  @Output() updateQuantity = new EventEmitter<{ productId: number; quantity: number }>();
  @Output() updateUnitPrice = new EventEmitter<{ productId: number; unitPrice: number }>();
  @Output() updateLineDiscount = new EventEmitter<{ productId: number; discountAmount: number }>();
  @Output() updateSaleDiscount = new EventEmitter<number>();
  @Output() removeItem = new EventEmitter<number>();
  @Output() notesChange = new EventEmitter<string>();
  @Output() selectLine = new EventEmitter<number>();
  @Output() checkout = new EventEmitter<void>();

  constructor() {
    afterRenderEffect(() => {
      const request = this.revealRequest();
      if (!request || request.productId !== this.activeProductId) return;

      const list = this.cartList?.nativeElement;
      const row = this.findRow(request.productId);
      if (!list || !row) return;

      // Read focus after rendering: the operator may have started editing since the request.
      const editor = list.ownerDocument.activeElement?.closest(
        'input, textarea, select, [contenteditable]:not([contenteditable="false"])',
      );
      const focusedRow = editor?.closest('.cart-row');
      if (focusedRow && list.contains(focusedRow) && focusedRow !== row) return;

      this.revealWithinList(list, row);
    });
  }

  focusQuantity(productId: number): boolean {
    const list = this.cartList?.nativeElement;
    const row = this.findRow(productId);
    const quantity = row?.querySelector<HTMLInputElement>('[data-cart-quantity] input');
    if (!list || !row || !quantity || quantity.disabled || quantity.readOnly) return false;

    this.revealWithinList(list, row, quantity);
    quantity.focus({ preventScroll: true });
    return list.ownerDocument.activeElement === quantity;
  }

  isQuantityInput(target: HTMLElement | null): boolean {
    return !!target?.matches('[data-cart-quantity] input')
      && this.cartList?.nativeElement.contains(target) === true;
  }

  private findRow(productId: number): HTMLElement | undefined {
    return this.cartList?.nativeElement.querySelector<HTMLElement>(
      `[data-cart-product-id="${productId}"]`,
    ) ?? undefined;
  }

  private revealWithinList(list: HTMLDivElement, row: HTMLElement, quantity?: HTMLInputElement): void {
    const height = list.clientHeight;
    if (!height || list.scrollHeight <= height) return;

    const top = list.getBoundingClientRect().top + list.clientTop;
    const bottom = top + height;
    const bounds = row.getBoundingClientRect();
    if (bounds.height > height && !quantity) {
      // An oversized row is already visible if it spans the viewport.
      if (bounds.top > top || bounds.bottom < bottom) list.scrollTop += bounds.top - top;
      return;
    }

    const target = bounds.height > height && quantity ? quantity.getBoundingClientRect() : bounds;
    if (target.top < top) list.scrollTop += target.top - top;
    else if (target.bottom > bottom) list.scrollTop += target.bottom - bottom;
  }

  get hasItems(): boolean {
    return this.items.length > 0;
  }

  lineSubtotal(item: CartItem): number {
    return item.quantity * item.unitPrice;
  }

  lineNetSubtotal(item: CartItem): number {
    return calculateLineNetSubtotal(item.quantity, item.unitPrice, item.discountAmount);
  }

  effectiveLine(item: CartItem): SalePreview['items'][number] | undefined {
    return this.fiscalPreview?.items.find(line => line.productId === item.productId);
  }

  lineTotal(item: CartItem): number | null {
    return this.effectiveLine(item)?.lineTotal ?? null;
  }

  vatLabel(item: CartItem): string {
    const line = this.effectiveLine(item);
    return line ? getVatCategoryOption(line.vatCategory).shortLabel : 'IVA pendiente';
  }

  isActive(item: CartItem): boolean {
    return item.productId === this.activeProductId;
  }

  maxQuantity(item: CartItem): number | null {
    if (!this.inventoryAvailable || (item.product.isActive && item.product.tracksInventory === false)) {
      return null;
    }

    return Math.max(item.stock, 1);
  }

  maxLineDiscount(item: CartItem): number {
    return this.lineSubtotal(item);
  }

  maxSaleDiscount(): number {
    return this.items.reduce((sum, item) => sum + this.lineNetSubtotal(item), 0);
  }
}
