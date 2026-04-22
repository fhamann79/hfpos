import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, ViewChild } from '@angular/core';
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
export class QuickProductSearchDialog implements AfterViewInit, OnChanges {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) products: PosProduct[] = [];
  @Input() inventoryAvailable = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() selectProduct = new EventEmitter<PosProduct>();
  @Output() unavailableProduct = new EventEmitter<PosProduct>();

  @ViewChild('searchInput') searchInput?: ElementRef<HTMLInputElement>;

  filter = '';
  highlightedIndex = 0;

  ngAfterViewInit(): void {
    this.focusInput();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.resetLookup();
    }
  }

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (value) {
      this.resetLookup();
    }
  }

  onFilterChange(value: string): void {
    this.filter = value;
    this.highlightedIndex = 0;
  }

  get filteredProducts(): PosProduct[] {
    const term = this.filter.trim().toLowerCase();
    const filtered = this.products
      .filter((product) => this.matchesTerm(product, term))
      .sort((a, b) => this.matchRank(a, term) - this.matchRank(b, term) || a.name.localeCompare(b.name));

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
    if (!this.inventoryAvailable || product.stock <= 0) {
      this.unavailableProduct.emit(product);
      return;
    }

    this.selectProduct.emit(product);
    this.close();
  }

  close(): void {
    this.visibleChange.emit(false);
  }

  private focusInput(): void {
    this.searchInput?.nativeElement.focus();
  }

  private resetLookup(): void {
    this.filter = '';
    this.highlightedIndex = 0;
    setTimeout(() => this.focusInput(), 0);
  }

  private matchesTerm(product: PosProduct, term: string): boolean {
    if (!term.length) {
      return true;
    }

    return this.searchText(product).some((value) => value.includes(term));
  }

  private matchRank(product: PosProduct, term: string): number {
    if (!term.length) {
      return 3;
    }

    if (this.sameIdentifier(product.barcode, term) || this.sameIdentifier(product.internalCode, term)) {
      return 0;
    }

    if (product.name.toLowerCase().startsWith(term)) {
      return 1;
    }

    return 2;
  }

  private sameIdentifier(value: string | null | undefined, term: string): boolean {
    return !!value && value.trim().toLowerCase() === term;
  }

  private searchText(product: PosProduct): string[] {
    return [product.name, product.barcode ?? '', product.internalCode ?? '']
      .map((value) => value.trim().toLowerCase())
      .filter((value) => value.length > 0);
  }
}
