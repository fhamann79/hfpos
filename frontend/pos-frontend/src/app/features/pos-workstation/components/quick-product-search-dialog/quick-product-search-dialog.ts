import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import { PosProduct } from '../../models/pos-product.model';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';

@Component({
  selector: 'app-quick-product-search-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, InputTextModule],
  templateUrl: './quick-product-search-dialog.html',
  styleUrl: './quick-product-search-dialog.scss',
})
export class QuickProductSearchDialog implements AfterViewInit, OnChanges, OnDestroy {
  private readonly catalogService = inject(PosProductCatalogService);
  private readonly remoteProducts = signal<PosProduct[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  private searchRequestId = 0;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

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

  ngOnDestroy(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.searchRequestId++;
  }

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (value) {
      this.resetLookup();
    } else {
      this.cancelPendingLookup();
    }
  }

  onFilterChange(value: string): void {
    this.filter = value;
    this.highlightedIndex = 0;
    this.scheduleLookup(value);
  }

  get filteredProducts(): PosProduct[] {
    return this.remoteProducts().slice(0, 20);
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

  getVatLabel(product: PosProduct): string {
    return getVatCategoryOption(product.vatCategory).shortLabel;
  }

  close(): void {
    this.cancelPendingLookup();
    this.visibleChange.emit(false);
  }

  private focusInput(): void {
    this.searchInput?.nativeElement.focus();
  }

  private resetLookup(): void {
    this.filter = '';
    this.highlightedIndex = 0;
    this.remoteProducts.set([]);
    this.errorMessage.set('');
    this.runLookup('', ++this.searchRequestId);
    setTimeout(() => this.focusInput(), 0);
  }

  private scheduleLookup(value: string): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }

    const requestId = ++this.searchRequestId;
    this.errorMessage.set('');
    this.loading.set(true);
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.runLookup(value.trim(), requestId);
    }, 150);
  }

  private runLookup(search: string, requestId: number): void {
    this.loading.set(true);
    this.catalogService.searchProducts(search, 30).subscribe({
      next: (products) => {
        if (requestId !== this.searchRequestId || !this.visible) {
          return;
        }

        this.remoteProducts.set(products);
        this.highlightedIndex = 0;
        this.loading.set(false);
      },
      error: () => {
        if (requestId !== this.searchRequestId || !this.visible) {
          return;
        }

        this.remoteProducts.set([]);
        this.loading.set(false);
        this.errorMessage.set('No se pudo buscar productos. Intenta nuevamente.');
      },
    });
  }

  private cancelPendingLookup(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
    this.searchRequestId++;
    this.loading.set(false);
  }
}
