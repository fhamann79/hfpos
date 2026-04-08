import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PosProduct } from '../../models/pos-product.model';

@Component({
  selector: 'app-product-search-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, CurrencyPipe, ButtonModule, InputTextModule, MessageModule],
  templateUrl: './product-search-panel.html',
  styleUrl: './product-search-panel.scss',
})
export class ProductSearchPanel {
  @Input({ required: true }) products: PosProduct[] = [];
  @Input({ required: true }) loading = false;
  @Input() errorMessage = '';
  @Input() canSell = false;
  @Input() searchTerm = '';

  @Output() searchTermChange = new EventEmitter<string>();
  @Output() addProduct = new EventEmitter<PosProduct>();
  @Output() quickSearch = new EventEmitter<void>();

  onSearchChange(value: string): void {
    this.searchTermChange.emit(value);
  }

  add(product: PosProduct): void {
    if (!this.canSell || product.stock <= 0) {
      return;
    }

    this.addProduct.emit(product);
  }

  openQuickSearch(): void {
    if (!this.canSell) {
      return;
    }

    this.quickSearch.emit();
  }
}
