import { CommonModule, CurrencyPipe } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
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
export class ProductSearchPanel implements AfterViewInit {
  @Input({ required: true }) products: PosProduct[] = [];
  @Input({ required: true }) loading = false;
  @Input() errorMessage = '';
  @Input() canSell = false;
  @Input() searchTerm = '';
  @Input() inventoryAvailable = false;
  @Input() inventoryErrorMessage = '';

  @Output() searchTermChange = new EventEmitter<string>();
  @Output() submitSearch = new EventEmitter<void>();
  @Output() addProduct = new EventEmitter<PosProduct>();
  @Output() quickSearch = new EventEmitter<void>();

  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;

  ngAfterViewInit(): void {
    if (this.canSell) {
      this.focusSearchInput();
    }
  }

  onSearchChange(value: string): void {
    this.searchTermChange.emit(value);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter') {
      return;
    }

    this.submitSearch.emit();
    event.preventDefault();
  }

  add(product: PosProduct): void {
    if (!this.canSell || !this.inventoryAvailable || product.stock <= 0) {
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

  focusSearchInput(): void {
    setTimeout(() => this.searchInput?.nativeElement.focus(), 0);
  }
}
