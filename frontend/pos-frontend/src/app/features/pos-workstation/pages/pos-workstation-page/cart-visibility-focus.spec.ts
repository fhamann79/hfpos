import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { SalePreview } from '../../models/checkout-request.model';
import { PosProduct } from '../../models/pos-product.model';
import { CheckoutIntentService } from '../../services/checkout-intent.service';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';
import { PosWorkstationPage } from './pos-workstation-page';

describe('POS cart visibility and keyboard focus', () => {
  let fixture: ComponentFixture<PosWorkstationPage>;
  let page: PosWorkstationPage;
  let host: HTMLElement;
  let search: HTMLInputElement;
  let lookup: Subject<PosProduct[]>;
  let preview: Subject<SalePreview>;
  const searchProducts = vi.fn();
  const previewSale = vi.fn();

  const product = (id: number): PosProduct => ({
    id, name: `Synthetic ${id}`, barcode: `SYN-${id}`, price: 2, stock: 10,
    isActive: true, vatCategory: ProductVatCategory.Vat15,
  });

  beforeEach(async () => {
    lookup = new Subject();
    preview = new Subject();
    searchProducts.mockReset().mockReturnValue(lookup);
    previewSale.mockReset().mockReturnValue(preview);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        providePrimeNG({ unstyled: true }),
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: {
          companyId: () => 1, establishmentId: () => 2, emissionPointId: () => 3,
          companyTimeZoneId: () => 'America/Guayaquil',
          me: () => ({ userId: 'synthetic' }),
        } },
        { provide: CheckoutIntentService, useValue: { load: () => null } },
        { provide: PosProductCatalogService, useValue: { searchProducts } },
        { provide: PosWorkstationService, useValue: { previewSale } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
    fixture = TestBed.createComponent(PosWorkstationPage);
    page = fixture.componentInstance;
    vi.spyOn(page, 'loadProducts').mockImplementation(() => undefined);
    vi.spyOn(page, 'loadCurrentCashSession').mockImplementation(() => undefined);
    vi.spyOn(page, 'loadSales').mockImplementation(() => undefined);
    page.inventoryAvailable.set(true);
    fixture.detectChanges();
    await new Promise<void>((resolve) => setTimeout(resolve, 0)); // Existing initial search focus.
    host = fixture.nativeElement;
    search = host.querySelector<HTMLInputElement>('input[aria-keyshortcuts="Alt+Q"]')!;
  });

  function key(target: HTMLElement, name: string, options: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...options });
    target.dispatchEvent(event);
    return event;
  }

  function quantityShortcut(target: HTMLElement): KeyboardEvent {
    return key(target, 'q', { altKey: true, code: 'KeyQ' });
  }

  async function finishPreview(items: number): Promise<void> {
    await vi.waitFor(() => expect(previewSale.mock.lastCall?.[0].items).toHaveLength(items));
    preview.next({
      fingerprint: 'SYNTHETIC', total: 4, fiscalDate: '2026-11-10', profileRevision: 1,
      grossSubtotal: 4, discountAmount: 0, subtotal: 4, taxAmount: 0,
      vat15Subtotal: 0, vat5Subtotal: 0, vat0Subtotal: 4, vatExemptSubtotal: 0,
      vatNotSubjectSubtotal: 0, items: [],
    });
    fixture.detectChanges();
    expect(page.fiscalPreviewCurrent()).toBe(true);
  }

  function renderCart() {
    page.addProduct(product(1));
    fixture.detectChanges();
    const list = host.querySelector<HTMLDivElement>('.cart-list')!;
    Object.defineProperties(list, { clientHeight: { value: 100 }, scrollHeight: { value: 400 } });
    vi.spyOn(list, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 50, 300, 100));
    // jsdom has no layout; row rectangles move with the actual list scrollTop.
    const rect = (id: number) => new DOMRect(0, 50 + (id - 1) * 85 - list.scrollTop, 300, 75);
    const measure = HTMLElement.prototype.getBoundingClientRect;
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return this.dataset['cartProductId']
        ? rect(Number(this.dataset['cartProductId'])) : measure.call(this);
    });
    return list;
  }

  afterEach(() => {
    fixture.destroy();
    vi.restoreAllMocks();
  });

  it('reveals new and repeated lines locally, and follows arrows without moving search focus', () => {
    const list = renderCart();
    search.focus();
    page.addProduct(product(2));
    page.addProduct(product(3));
    fixture.detectChanges();
    expect(list.scrollTop).toBe(145);
    expect(host.querySelector('.cart-row--active')?.getAttribute('data-cart-product-id')).toBe('3');

    // A repeated scan of the same active product must also reveal it after manual scrolling.
    list.scrollTop = 0;
    page.allProducts.set([product(1), product(3)]);
    page.onSearchTermChange('SYN-3');
    fixture.detectChanges();
    expect(list.scrollTop).toBe(145);
    page.onSearchTermChange('SYN-1');
    fixture.detectChanges();
    expect(list.scrollTop).toBe(0);
    expect(page.cart()[0].quantity).toBe(2);
    expect(host.querySelector('.cart-row--active')?.getAttribute('data-cart-product-id')).toBe('1');

    expect(key(search, 'ArrowDown').defaultPrevented).toBe(true);
    fixture.detectChanges();
    expect(list.scrollTop).toBe(60);
    expect(key(search, 'ArrowUp').defaultPrevented).toBe(true);
    fixture.detectChanges();
    expect(list.scrollTop).toBe(0);
    expect(document.activeElement).toBe(search);

    const rowButton = host.querySelector<HTMLButtonElement>('[data-cart-product-id="1"] .line-total button')!;
    rowButton.focus();
    expect(key(rowButton, 'ArrowDown').defaultPrevented).toBe(true);
    fixture.detectChanges();
    expect(list.scrollTop).toBe(60);
    expect(document.activeElement).toBe(rowButton);
  });

  it('focuses the real quantity editor intentionally and commits on return to the scanner', async () => {
    const list = renderCart();
    page.addProduct(product(2));
    fixture.detectChanges();
    await fixture.whenStable();
    list.scrollTop = 0;
    search.focus();
    expect(quantityShortcut(search).defaultPrevented).toBe(true);
    const quantity = host.querySelector<HTMLInputElement>('#qty-2')!;
    expect(document.activeElement).toBe(quantity);
    expect(quantity.getAttribute('aria-keyshortcuts')).toBe('Alt+Q');
    expect(list.scrollTop).toBe(60);
    expect(key(quantity, 'ArrowDown').defaultPrevented).toBe(true); // PrimeNG, not cart navigation.
    expect(page.activeCartProductId()).toBe(2);

    quantity.value = '4';
    quantity.dispatchEvent(new Event('input', { bubbles: true }));
    expect(quantityShortcut(quantity).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(search);
    expect(page.cart().find(item => item.productId === 2)?.quantity).toBe(4);
    expect(key(search, '+').defaultPrevented).toBe(true);
    expect(page.cart().find(item => item.productId === 2)?.quantity).toBe(5);
    expect(key(search, 'Delete').defaultPrevented).toBe(true);
    expect(page.cart().map(item => item.productId)).toEqual([1]);
  });

  it('does not steal focus or replay scroll when a remote scan or fiscal preview finishes during an edit', async () => {
    const list = renderCart();
    search.focus();
    search.value = 'SYN-3';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(key(search, 'ArrowDown').defaultPrevented).toBe(false);
    expect(quantityShortcut(search).defaultPrevented).toBe(false);
    await vi.waitFor(() => expect(searchProducts).toHaveBeenCalledWith('SYN-3', 30));

    const price = host.querySelector<HTMLInputElement>('#price-1')!;
    price.focus();
    lookup.next([product(3)]);
    fixture.detectChanges();
    expect(page.cart().map(item => item.productId)).toEqual([1, 3]);
    expect(list.scrollTop).toBe(0);
    expect(document.activeElement).toBe(price);
    expect(quantityShortcut(price).defaultPrevented).toBe(false);

    search.focus();
    await finishPreview(2);
    expect(list.scrollTop).toBe(0);
    expect(document.activeElement).toBe(search);
  });

  it('leaves modal keys, checkout lock, and ordinary scanner Enter intact', () => {
    renderCart();
    const modal = document.createElement('div');
    modal.setAttribute('role', 'dialog');
    const button = document.createElement('button');
    modal.append(button);
    host.append(modal);
    button.focus();
    expect(key(button, 'Delete').defaultPrevented).toBe(false);
    expect(page.cart()).toHaveLength(1);
    page.receiptVisible.set(true);
    search.focus();
    expect(key(search, 'ArrowDown').defaultPrevented).toBe(false);
    expect(quantityShortcut(search).defaultPrevented).toBe(false);
    page.receiptVisible.set(false);
    page.checkoutLoading.set(true);
    expect(quantityShortcut(search).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(search);
    page.checkoutLoading.set(false);
    page.searchTerm.set('Synthetic');
    page.allProducts.set([product(2)]);
    fixture.detectChanges();
    expect(key(search, 'Enter').defaultPrevented).toBe(true);
    expect(page.cart().map(item => item.productId)).toEqual([1, 2]);
    expect(document.activeElement).toBe(search);
  });

  it('does not confirm checkout from its cash input with the quantity shortcut; plain Enter still confirms', async () => {
    renderCart();
    await finishPreview(1);
    const confirm = vi.spyOn(page, 'confirmCheckout').mockImplementation(() => undefined);
    page.checkoutVisible.set(true);
    fixture.detectChanges();
    const cash = document.querySelector<HTMLInputElement>('#checkout-cash-received')!;
    cash.focus();
    expect(quantityShortcut(cash).defaultPrevented).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(cash);
    expect(page.checkoutVisible()).toBe(true);
    expect(key(cash, 'Enter').defaultPrevented).toBe(true);
    expect(confirm).toHaveBeenCalledOnce();
  });

  it('does not pick or close a real F2 search with the quantity shortcut; plain Enter still picks', () => {
    renderCart();
    expect(key(search, 'F2').defaultPrevented).toBe(true);
    fixture.detectChanges();
    lookup.next([product(2)]);
    fixture.detectChanges();
    const quickSearch = document.querySelector<HTMLInputElement>('.quick-search-input')!;
    quickSearch.focus();
    expect(quantityShortcut(quickSearch).defaultPrevented).toBe(false);
    expect(page.cart().map(item => item.productId)).toEqual([1]);
    expect(page.quickSearchVisible()).toBe(true);
    expect(document.activeElement).toBe(quickSearch);
    expect(key(quickSearch, 'Enter').defaultPrevented).toBe(true);
    expect(page.cart().map(item => item.productId)).toEqual([1, 2]);
    expect(page.quickSearchVisible()).toBe(false);
  });
});
