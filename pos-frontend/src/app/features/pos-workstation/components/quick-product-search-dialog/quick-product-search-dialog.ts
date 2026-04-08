import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { PosProduct } from '../../models/pos-product.model';

@Component({
  selector: 'app-quick-product-search-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, InputTextModule],
  templateUrl: './quick-product-search-dialog.html',
  styleUrl: './quick-product-search-dialog.scss',
})
export class QuickProductSearchDialog implements AfterViewInit {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) products: PosProduct[] = [];

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() selectProduct = new EventEmitter<PosProduct>();

  @ViewChild('searchInput') searchInput?: ElementRef<HTMLInputElement>;

  filter = '';
  highlightedIndex = 0;

  ngAfterViewInit(): void {
    this.focusInput();
  }

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (value) {
      this.filter = '';
      this.highlightedIndex = 0;
      setTimeout(() => this.focusInput(), 0);
    }
  }

  get filteredProducts(): PosProduct[] {
    const term = this.filter.trim().toLowerCase();
    const filtered = this.products.filter((product) =>
      product.name.toLowerCase().includes(term)
    );

    return filtered.slice(0, 20);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.close();
      return;
    }

    if (event.key === 'ArrowDown') {
      this.highlightedIndex = Math.min(this.highlightedIndex + 1, Math.max(this.filteredProducts.length - 1, 0));
      event.preventDefault();
      return;
    }

    if (event.key === 'ArrowUp') {
      this.highlightedIndex = Math.max(this.highlightedIndex - 1, 0);
      event.preventDefault();
      return;
    }

    if (event.key === 'Enter') {
      const product = this.filteredProducts[this.highlightedIndex];
      if (product) {
        this.pick(product);
      }
      event.preventDefault();
    }
  }

  pick(product: PosProduct): void {
    this.selectProduct.emit(product);
    this.close();
  }

  close(): void {
    this.visibleChange.emit(false);
  }

  private focusInput(): void {
    this.searchInput?.nativeElement.focus();
  }
}
