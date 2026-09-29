import { CommonModule, CurrencyPipe } from '@angular/common';
import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import { PosProduct } from '../../models/pos-product.model';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';

@Component({
  selector: 'app-product-search-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, CurrencyPipe, ButtonModule, InputTextModule, MessageModule],
  templateUrl: './product-search-panel.html',
  styleUrl: './product-search-panel.scss',
})
export class ProductSearchPanel implements AfterViewInit, OnDestroy {
  private readonly catalogService = inject(PosProductCatalogService);
  private readonly remoteProducts = signal<PosProduct[]>([]);
  private readonly remoteLoading = signal(false);
  private readonly remoteError = signal('');
  private searchRequestId = 0;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

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

  get hasSearchTerm(): boolean {
    return this.searchTerm.trim().length > 0;
  }

  get results(): PosProduct[] {
    return this.remoteProducts();
  }

  get isLoading(): boolean {
    return this.loading || this.remoteLoading();
  }

  get effectiveErrorMessage(): string {
    return this.remoteError() || this.errorMessage;
  }

  ngAfterViewInit(): void {
    if (this.canSell) {
      this.focusSearchInput();
    }
  }

  ngOnDestroy(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.searchRequestId++;
  }

  onSearchChange(value: string): void {
    this.searchTermChange.emit(value);
    this.scheduleRemoteSearch(value);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter') {
      return;
    }

    const term = this.searchTerm.trim();
    const exact = this.findExactIdentifierMatch(term);
    const single = this.results.length === 1 ? this.results[0] : null;

    if (exact && this.canAdd(exact)) {
      this.addAndClear(exact);
    } else if (single && this.canAdd(single)) {
      this.addAndClear(single);
    } else {
      this.submitSearch.emit();
    }

    event.preventDefault();
  }

  add(product: PosProduct): void {
    if (!this.canAdd(product)) {
      return;
    }

    this.addProduct.emit(product);
  }

  getVatLabel(product: PosProduct): string {
    return getVatCategoryOption(product.vatCategory).shortLabel;
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

  private scheduleRemoteSearch(value: string): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }

    const term = value.trim();
    const requestId = ++this.searchRequestId;
    this.remoteError.set('');

    if (!term) {
      this.remoteProducts.set([]);
      this.remoteLoading.set(false);
      return;
    }

    this.remoteLoading.set(true);
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.catalogService.searchProducts(term, 30).subscribe({
        next: (products) => {
          if (requestId !== this.searchRequestId || term !== this.searchTerm.trim()) {
            return;
          }

          this.remoteProducts.set(products);
          this.remoteLoading.set(false);

          const exact = this.findExactIdentifierMatch(term);
          if (exact && this.canAdd(exact)) {
            this.addAndClear(exact);
          }
        },
        error: () => {
          if (requestId !== this.searchRequestId) {
            return;
          }

          this.remoteProducts.set([]);
          this.remoteLoading.set(false);
          this.remoteError.set('No se pudo buscar productos. Intenta nuevamente.');
        },
      });
    }, 150);
  }

  private addAndClear(product: PosProduct): void {
    this.addProduct.emit(product);
    this.searchRequestId++;
    this.remoteProducts.set([]);
    this.remoteLoading.set(false);
    this.remoteError.set('');
    this.searchTermChange.emit('');
    this.focusSearchInput();
  }

  private canAdd(product: PosProduct): boolean {
    return this.canSell && this.inventoryAvailable && product.stock > 0;
  }

  private findExactIdentifierMatch(term: string): PosProduct | null {
    const normalized = term.trim().toLowerCase();
    if (!normalized) {
      return null;
    }

    return this.results.find((product) =>
      product.barcode?.trim().toLowerCase() === normalized
      || product.internalCode?.trim().toLowerCase() === normalized
    ) ?? null;
  }
}
