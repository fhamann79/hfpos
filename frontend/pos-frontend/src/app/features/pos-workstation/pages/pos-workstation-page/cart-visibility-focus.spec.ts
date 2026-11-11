import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { CashSession, CashSessionStatus } from '../../../cash-sessions/models/cash-session.model';
import { CheckoutIntent } from '../../models/checkout-intent.model';
import { SalePreview } from '../../models/checkout-request.model';
import { PosProduct } from '../../models/pos-product.model';
import { SaleDocumentType } from '../../models/sale-document.model';
import { SaleListItem } from '../../models/sale-list-item.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { CheckoutIntentService } from '../../services/checkout-intent.service';
import { PosCustomerService } from '../../services/pos-customer.service';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosQuickProductsService } from '../../services/pos-quick-products.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';
import { PosWorkstationPage } from './pos-workstation-page';
import { PosQuickProducts } from '../../components/product-search-panel/pos-quick-products';

describe('POS cart visibility and keyboard focus', () => {
  let fixture: ComponentFixture<PosWorkstationPage>;
  let page: PosWorkstationPage;
  let host: HTMLElement;
  let search: HTMLInputElement;
  let lookup: Subject<PosProduct[]>;
  let preview: Subject<SalePreview>;
  const searchProducts = vi.fn();
  const refreshCartProducts = vi.fn();
  const getFavorites = vi.fn();
  const getTopSellers = vi.fn();
  const saveFavorites = vi.fn();
  const previewSale = vi.fn();
  const createCustomer = vi.fn();
  const loadIntent = vi.fn<() => CheckoutIntent | null>();

  const product = (id: number): PosProduct => ({
    id, name: `Synthetic ${id}`, barcode: `SYN-${id}`, price: 2, stock: 10,
    isActive: true, vatCategory: ProductVatCategory.Vat15,
  });

  beforeEach(async () => {
    lookup = new Subject();
    preview = new Subject();
    searchProducts.mockReset().mockReturnValue(lookup);
    refreshCartProducts.mockReset().mockReturnValue(of([]));
    getFavorites.mockReset().mockReturnValue(of({ revision: 0, productIds: [] }));
    getTopSellers.mockReset().mockReturnValue(of([]));
    saveFavorites.mockReset();
    previewSale.mockReset().mockReturnValue(preview);
    createCustomer.mockReset();
    loadIntent.mockReset().mockReturnValue(null);
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
        { provide: CheckoutIntentService, useValue: { load: loadIntent } },
        { provide: PosCustomerService, useValue: { search: () => of([]), create: createCustomer } },
        { provide: PosProductCatalogService, useValue: { searchProducts, refreshCartProducts } },
        { provide: PosQuickProductsService, useValue: { getFavorites, getTopSellers, saveFavorites } },
        { provide: PosWorkstationService, useValue: {
          previewSale, resolveBusinessError: () => 'Perfil fiscal pendiente',
        } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
    await renderPage();
  });

  async function renderPage(): Promise<void> {
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
  }

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

  async function openCheckout(): Promise<HTMLElement> {
    page.currentCashSession.set({
      id: 1, status: CashSessionStatus.Open, openingAmount: 0, expectedCashAmount: 0,
      openedAt: '2026-11-10T10:00:00Z', openBusinessDate: '2026-11-10',
      openTimeZoneIdSnapshot: 'America/Guayaquil',
    } as CashSession);
    page.addProduct(product(1));
    fixture.detectChanges();
    await finishPreview(1);
    search.focus();
    key(search, 'F9');
    fixture.detectChanges();
    expect(page.checkoutVisible()).toBe(true);
    const dialog = host.querySelector<HTMLElement>('app-checkout-confirm-dialog [role="dialog"]')!;
    dialog.querySelector<HTMLInputElement>('#checkout-cash-received')!.focus();
    return dialog;
  }

  afterEach(() => {
    fixture.destroy();
    vi.restoreAllMocks();
  });

  it('keeps closed cash visible and presents one recoverable fiscal status for the whole cart', async () => {
    page.addProduct(product(1));
    page.addProduct(product(2));
    fixture.detectChanges();
    expect(host.querySelector('.cash-context--blocked')?.textContent).toContain('Debes abrir caja');
    expect(host.querySelector<HTMLButtonElement>('button[aria-keyshortcuts="F9"]')?.disabled).toBe(true);
    expect(host.querySelector('.pos-page__header button[aria-keyshortcuts="F2"]')).not.toBeNull();
    expect(host.querySelector('.pos-page__header button[aria-keyshortcuts="F4"]')).not.toBeNull();
    expect(host.querySelector('.cart-shortcuts')?.textContent).toContain('Alt+Q');
    expect(host.querySelector('.cart-shortcuts')?.textContent).toContain('↑');
    expect(host.querySelector('.cart-shortcuts')?.textContent).toContain('↓');
    expect(host.querySelectorAll('.summary .fiscal-status')).toHaveLength(1);
    expect(host.querySelector('.fiscal-status')?.textContent).toContain('Calculando impuestos');
    expect(host.querySelector('.cart-list')?.textContent).not.toMatch(/pendiente/i);

    await vi.waitFor(() => expect(previewSale.mock.lastCall?.[0].items).toHaveLength(2));
    preview.error(new Error('Synthetic fiscal error'));
    fixture.detectChanges();
    expect(host.querySelectorAll('.summary .fiscal-status')).toHaveLength(1);
    expect(host.querySelector('.fiscal-status')?.textContent).toContain('Perfil fiscal pendiente');
    const retry = vi.spyOn(page, 'refreshFiscalPreview');
    host.querySelector<HTMLButtonElement>('.fiscal-status button')!.click();
    expect(retry).toHaveBeenCalledOnce();
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

  it('adds a favorite via the current product lookup and returns pointer focus to the scanner', () => {
    const service = { ...product(6), tracksInventory: false, stock: 0 };
    getFavorites.mockReturnValue(of({ revision: 3, productIds: [6] }));
    refreshCartProducts.mockReturnValueOnce(of([service])).mockReturnValueOnce(of([{ ...service, price: 3 }]));
    host.querySelector<HTMLButtonElement>('[aria-label="Accesos rápidos"] button')!.click();
    fixture.detectChanges();
    const tile = host.querySelector<HTMLButtonElement>('.product-tile')!;
    tile.focus();
    expect(key(tile, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    tile.dispatchEvent(new MouseEvent('click', { detail: 1, bubbles: true }));
    fixture.detectChanges();
    expect(page.cart()[0]).toMatchObject({ productId: 6, quantity: 1, unitPrice: 3 });
    expect(document.activeElement).toBe(search);

    page.allProducts.set([service]);
    page.onSearchTermChange('SYN-6');
    fixture.detectChanges();
    expect(page.cart()[0].quantity).toBe(2);
    expect(document.activeElement).toBe(search);
  });

  it('does not replay a delayed tile selection after recovery starts and leaves the cart unchanged', () => {
    const current = new Subject<PosProduct[]>();
    getFavorites.mockReturnValue(of({ revision: 3, productIds: [6] }));
    refreshCartProducts.mockReturnValueOnce(of([product(6)])).mockReturnValueOnce(current);
    host.querySelector<HTMLButtonElement>('[aria-label="Accesos rápidos"] button')!.click();
    fixture.detectChanges();
    const tile = host.querySelector<HTMLButtonElement>('.product-tile')!;
    tile.click();
    tile.click();
    expect(refreshCartProducts).toHaveBeenCalledTimes(2);
    page.pendingCheckout.set({ requestId: 'synthetic-pending', items: [] });
    fixture.detectChanges();
    current.next([product(6)]);
    expect(page.cart()).toHaveLength(0);
    expect(tile.getAttribute('aria-disabled')).toBe('true');
  });

  it('returns keyboard focus after a delayed favorite add so the next scan adds a different product', () => {
    const current = new Subject<PosProduct[]>();
    getFavorites.mockReturnValue(of({ revision: 3, productIds: [6] }));
    refreshCartProducts.mockReturnValueOnce(of([product(6)])).mockReturnValueOnce(current);
    host.querySelector<HTMLButtonElement>('#pos-favorites-tab')!.click();
    fixture.detectChanges();
    const tile = host.querySelector<HTMLButtonElement>('.product-tile')!;
    tile.focus();
    tile.click(); // Native keyboard button activation emits a click with detail zero.
    expect(key(tile, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    tile.click();
    expect(refreshCartProducts).toHaveBeenCalledTimes(2);
    expect(page.cart()).toHaveLength(0);

    current.next([product(6)]);
    expect(document.activeElement).toBe(search);
    expect(key(search, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    page.allProducts.set([product(7)]);
    const scannerTarget = document.activeElement as HTMLInputElement;
    scannerTarget.value = 'SYN-7';
    scannerTarget.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    key(scannerTarget, 'Enter');
    expect(page.cart().map(item => [item.productId, item.quantity])).toEqual([[6, 1], [7, 1]]);
  });

  it('does not steal focus if the operator moves it while a keyboard favorite lookup is pending', () => {
    page.addProduct(product(1));
    const current = new Subject<PosProduct[]>();
    getFavorites.mockReturnValue(of({ revision: 3, productIds: [6] }));
    refreshCartProducts.mockReturnValueOnce(of([product(6)])).mockReturnValueOnce(current);
    host.querySelector<HTMLButtonElement>('#pos-favorites-tab')!.click();
    fixture.detectChanges();
    const tile = host.querySelector<HTMLButtonElement>('.product-tile')!;
    tile.focus();
    tile.click();
    const price = host.querySelector<HTMLInputElement>('#price-1')!;
    price.focus();
    current.next([product(6)]);
    expect(page.cart().map(item => item.productId)).toEqual([1, 6]);
    expect(document.activeElement).toBe(price);
  });

  it('keeps the ranking stable during a sale and disables editing and refreshing', () => {
    getTopSellers.mockReturnValue(of([5, 4]));
    refreshCartProducts.mockReturnValue(of([product(4), product(5)]));
    const top = host.querySelector<HTMLButtonElement>('#pos-top-tab')!;
    top.click();
    fixture.detectChanges();
    expect([...host.querySelectorAll('.product-tile strong')].map(tile => tile.textContent)).toEqual(['Synthetic 5', 'Synthetic 4']);
    page.addProduct(product(1));
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('[aria-label="Actualizar más vendidos"]')!.disabled).toBe(true);
    host.querySelector<HTMLButtonElement>('#pos-favorites-tab')!.click();
    fixture.detectChanges();
    expect(host.querySelector<HTMLButtonElement>('[aria-label="Editar favoritos"]')!.disabled).toBe(true);
    top.click();
    expect(getTopSellers).toHaveBeenCalledTimes(1);
  });

  it('protects the editor from checkout keys and requires an explicit reload after a conflict', () => {
    const editor = fixture.debugElement.query(By.directive(PosQuickProducts)).componentInstance as PosQuickProducts;
    getFavorites.mockReturnValue(of({ revision: 3, productIds: [4, 5] }));
    refreshCartProducts.mockReturnValue(of([product(4), product(5)]));
    saveFavorites.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    host.querySelector<HTMLButtonElement>('[aria-label="Editar favoritos"]')!.click();
    fixture.detectChanges();
    expect(page.quickProductsEditorVisible()).toBe(true);
    key(search, 'F2');
    key(search, 'F4');
    key(search, 'F8');
    key(search, 'F9');
    key(search, 'F12');
    expect(page.quickSearchVisible()).toBe(false);
    expect(page.customerSelectorVisible()).toBe(false);
    expect(page.recentSalesVisible()).toBe(false);
    expect(page.checkoutVisible()).toBe(false);

    editor.moveFavorite(1, -1);
    editor.save();
    expect(saveFavorites).toHaveBeenCalledExactlyOnceWith({ revision: 3, productIds: [5, 4] });
    expect(editor.stale()).toBe(true);
    editor.save();
    expect(saveFavorites).toHaveBeenCalledTimes(1);
    getFavorites.mockReturnValue(of({ revision: 4, productIds: [5] }));
    editor.reloadEditor();
    expect(editor.stale()).toBe(false);
    expect(editor.draftIds()).toEqual([5]);
    editor.closeEditor();
    expect(page.quickProductsEditorVisible()).toBe(false);
  });

  it('unblocks keyboard shortcuts when a catalog failure unmounts the favorites editor', () => {
    host.querySelector<HTMLButtonElement>('[aria-label="Editar favoritos"]')!.click();
    fixture.detectChanges();
    expect(page.quickProductsEditorVisible()).toBe(true);

    page.inventoryAvailable.set(false);
    fixture.detectChanges();
    expect(page.quickProductsEditorVisible()).toBe(false);

    page.inventoryAvailable.set(true);
    fixture.detectChanges();
    key(search, 'F2');
    expect(page.quickSearchVisible()).toBe(true);
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

  it.each(['Escape', 'Cancelar', 'Cerrar cobro'])('restores scan focus after dismissing F9 with %s', async (action) => {
    const dialog = await openCheckout();
    const footer = dialog.querySelector<HTMLElement>('[data-pc-section="footer"]')!;
    const cancel = footer.querySelector<HTMLButtonElement>('button[aria-keyshortcuts="Escape"]')!;
    expect(cancel.textContent).toContain('Cancelar');
    expect(footer.querySelector('button[aria-keyshortcuts="F12"]')?.textContent).toContain('Confirmar');
    expect(dialog.querySelector('.confirm-content button[aria-keyshortcuts="F12"]')).toBeNull();

    if (action === 'Escape') key(cancel, 'Escape');
    else if (action === 'Cancelar') cancel.click();
    else dialog.querySelector<HTMLButtonElement>('button[aria-label="Cerrar cobro"]')!.click();

    expect(page.checkoutVisible()).toBe(false);
    expect(document.activeElement).not.toBe(search);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(document.activeElement).toBe(search);
    });
  });

  it('does not focus the scanner after a programmatic close or when another dialog opens during dismissal', async () => {
    await openCheckout();
    const focusSearch = vi.spyOn(search, 'focus');
    const afterHide = vi.spyOn(page, 'onCheckoutHidden');
    page.checkoutVisible.set(false);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(afterHide).toHaveBeenCalledOnce();
    });
    expect(focusSearch).not.toHaveBeenCalled();

    key(search, 'F9');
    fixture.detectChanges();
    const dialog = host.querySelector<HTMLElement>('app-checkout-confirm-dialog [role="dialog"]')!;
    key(dialog.querySelector<HTMLInputElement>('#checkout-cash-received')!, 'Escape');
    page.recentSalesVisible.set(true);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(afterHide).toHaveBeenCalledTimes(2);
    });
    expect(page.recentSalesVisible()).toBe(true);
    expect(focusSearch).not.toHaveBeenCalled();
  });

  it('keeps one confirmation path per gesture and locks the footer and Escape while loading', async () => {
    const dialog = await openCheckout();
    const confirm = vi.spyOn(page, 'confirmCheckout').mockImplementation(() => undefined);
    const button = dialog.querySelector<HTMLButtonElement>('button[aria-keyshortcuts="F12"]')!;
    const cancel = dialog.querySelector<HTMLButtonElement>('button[aria-keyshortcuts="Escape"]')!;
    const cash = dialog.querySelector<HTMLInputElement>('#checkout-cash-received')!;
    expect(key(cash, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    expect(key(button, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    key(button, 'Enter');
    expect(confirm).not.toHaveBeenCalled(); // Native button activation owns Enter in the footer.
    button.click();
    expect(confirm).toHaveBeenCalledOnce();
    key(cash, 'F12');
    expect(confirm).toHaveBeenCalledTimes(2);
    key(cash, 'F12', { repeat: true });
    expect(confirm).toHaveBeenCalledTimes(2);

    page.checkoutLoading.set(true);
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
    expect(cancel.disabled).toBe(true);
    expect(dialog.querySelector('button[aria-label="Cerrar cobro"]')).toBeNull();
    button.click();
    cancel.click();
    key(cancel, 'Escape');
    key(cash, 'Enter');
    expect(page.checkoutVisible()).toBe(true);
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it('blocks competing shortcuts during checkout and only lets F12 confirm from inside checkout', async () => {
    const dialog = await openCheckout();
    const confirm = vi.spyOn(page, 'confirmCheckout').mockImplementation(() => undefined);
    const cash = dialog.querySelector<HTMLInputElement>('#checkout-cash-received')!;
    for (const shortcut of ['F2', 'F4', 'F8']) {
      key(cash, shortcut);
    }
    expect(page.quickSearchVisible()).toBe(false);
    expect(page.customerSelectorVisible()).toBe(false);
    expect(page.recentSalesVisible()).toBe(false);
    for (const shortcut of ['F2', 'F4']) {
      expect(host.querySelector<HTMLButtonElement>(`button[aria-keyshortcuts="${shortcut}"]`)?.disabled).toBe(true);
    }
    key(search, 'F12');

    // Exercise the target guard even if another dialog becomes visible unexpectedly.
    page.quickSearchVisible.set(true);
    fixture.detectChanges();
    key(host.querySelector<HTMLInputElement>('.quick-search-input')!, 'F12');
    page.quickSearchVisible.set(false);
    page.customerSelectorVisible.set(true);
    fixture.detectChanges();
    key(host.querySelector<HTMLInputElement>('input[aria-label="Buscar cliente"]')!, 'F12');
    expect(confirm).not.toHaveBeenCalled();
    page.customerSelectorVisible.set(false);
    fixture.detectChanges();

    key(cash, 'F12');
    expect(confirm).toHaveBeenCalledOnce();
  });

  it.each([
    { first: 'F4', second: 'F2', input: 'input[aria-label="Buscar cliente"]' },
    { first: 'F2', second: 'F4', input: '.quick-search-input' },
  ])('keeps $first active when $second or F8 is pressed, then restores scanner focus', async ({ first, second, input }) => {
    key(search, first);
    fixture.detectChanges();
    const active = host.querySelector<HTMLInputElement>(input)!;
    await vi.waitFor(() => expect(document.activeElement).toBe(active));

    key(active, second);
    key(active, 'F8');
    fixture.detectChanges();
    expect(page.quickSearchVisible()).toBe(first === 'F2');
    expect(page.customerSelectorVisible()).toBe(first === 'F4');
    expect(page.recentSalesVisible()).toBe(false);
    expect(host.querySelectorAll('[role="dialog"]')).toHaveLength(1);
    expect(host.querySelector<HTMLButtonElement>(`button[aria-keyshortcuts="${second}"]`)?.disabled).toBe(true);
    expect(document.activeElement).toBe(active);

    const confirm = vi.spyOn(page, 'confirmCheckout');
    key(active, 'F12');
    key(active, 'F12', { repeat: true });
    expect(confirm).not.toHaveBeenCalled();
    expect(page.checkoutVisible()).toBe(false);

    key(active, 'Escape');
    fixture.detectChanges();
    await vi.waitFor(() => expect(document.activeElement).toBe(search));
    expect(page.quickSearchVisible()).toBe(false);
    expect(page.customerSelectorVisible()).toBe(false);
    expect(host.querySelector<HTMLButtonElement>(`button[aria-keyshortcuts="${second}"]`)?.disabled).toBe(false);
  });

  it('routes the first F4 Escape back to customer lookup and the second back to the scanner', async () => {
    key(search, 'F4');
    fixture.detectChanges();
    const lookupInput = () => host.querySelector<HTMLInputElement>('input[aria-label="Buscar cliente"]')!;
    await vi.waitFor(() => expect(document.activeElement).toBe(lookupInput()));
    const dialog = lookupInput().closest('[role="dialog"]')!;
    const newCustomer = Array.from(dialog.querySelectorAll('button')).find(button => button.textContent?.trim() === 'Nuevo')!;
    newCustomer.click();
    fixture.detectChanges();
    const name = dialog.querySelector<HTMLInputElement>('input[name="name"]')!;
    await vi.waitFor(() => expect(document.activeElement).toBe(name));

    expect(key(name, 'Escape').defaultPrevented).toBe(true);
    fixture.detectChanges();
    expect(page.customerSelectorVisible()).toBe(true);
    expect(dialog.querySelector('.create-form')).toBeNull();
    await vi.waitFor(() => expect(document.activeElement).toBe(lookupInput()));
    key(lookupInput(), 'Escape', { repeat: true });
    expect(page.customerSelectorVisible()).toBe(true);
    expect(key(lookupInput(), 'Escape').defaultPrevented).toBe(true);
    fixture.detectChanges();
    expect(page.customerSelectorVisible()).toBe(false);
    await vi.waitFor(() => expect(document.activeElement).toBe(search));
    expect(createCustomer).not.toHaveBeenCalled();
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

  it.each([
    { action: 'Escape', pending: false }, { action: 'X', pending: false },
    { action: 'Escape', pending: true }, { action: 'X', pending: true },
  ])('restores scanner focus after F8 $action (pending=$pending)', async ({ action, pending }) => {
    if (pending) page.pendingCheckout.set({ requestId: 'SYNTHETIC-PENDING', items: [] });
    search.focus();
    key(search, 'F8');
    fixture.detectChanges();
    expect(page.recentSalesVisible()).toBe(true);
    const button = host.querySelector<HTMLButtonElement>('button[aria-keyshortcuts="F8"]')!;
    expect(button.textContent).toContain('Ventas recientes (F8)');
    const dialog = host.querySelector('app-recent-sales-panel')!.closest<HTMLElement>('[role="dialog"]')!;
    const close = dialog.querySelector<HTMLButtonElement>('[data-pc-section="header"] button')!;
    close.focus();
    const confirm = vi.spyOn(page, 'confirmCheckout');
    key(close, 'F12');
    key(close, 'F12', { repeat: true });
    key(close, 'F9');
    expect(confirm).not.toHaveBeenCalled();
    expect(page.checkoutVisible()).toBe(false);

    if (action === 'Escape') key(close, 'Escape');
    else close.click();
    expect(page.recentSalesVisible()).toBe(false);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(document.activeElement).toBe(search);
    });
    expect(!!page.pendingCheckout()).toBe(pending);

    button.click();
    fixture.detectChanges();
    expect(page.recentSalesVisible()).toBe(true);
  });

  it.each(['detail', 'void'])('does not steal focus from %s when history finishes hiding', async (destination) => {
    key(search, 'F8');
    fixture.detectChanges();
    const focus = vi.spyOn(search, 'focus');
    const hidden = vi.spyOn(page, 'onRecentSalesHidden');
    if (destination === 'detail') {
      vi.spyOn(page, 'openSaleDetail').mockImplementation(() => page.saleDetailVisible.set(true));
      page.openSaleDetailFromRecent(1);
    } else {
      page.openVoidDialogFromRecent({ id: 1 } as SaleListItem);
    }
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(hidden).toHaveBeenCalledOnce();
    });
    expect(focus).not.toHaveBeenCalled();
  });

  it('keeps verified amounts and distinguishes recovery from normal checkout', async () => {
    await openCheckout();
    expect(host.querySelector('.grand-total strong')?.textContent).toBe('$4.00');
    expect(host.querySelector('.checkout-zone button')?.textContent?.trim()).toBe('Cobrar (F9)');

    page.checkoutVisible.set(false);
    page.pendingCheckout.set({ requestId: 'SYNTHETIC-PENDING', items: [] });
    fixture.detectChanges();
    expect(host.querySelector('.summary-lines')?.textContent).toContain('Subtotal bruto');
    expect(host.querySelector('.grand-total strong')?.textContent).toBe('$4.00');
    expect(host.querySelector('.checkout-zone button')?.textContent?.trim()).toBe('Recuperar cobro (F9)');
  });

  it('does not show fallback aggregates for a restored checkout with cart lines and no preview', async () => {
    const line = { productId: 1, quantity: 2, unitPrice: 2, discountAmount: 0 };
    const documentType = SaleDocumentType.Ticket;
    const paymentMethod = SalePaymentMethod.Cash;
    const request = { requestId: '00000000-0000-4000-8000-000000000001', documentType, paymentMethod, items: [line] };
    loadIntent.mockReturnValue({
      scope: '1:synthetic:2:3',
      request,
      draft: {
        cart: [{ ...line, product: product(1), productName: 'Synthetic 1', stock: 10 }],
        customer: null, discountAmount: 0, notes: '', documentType, paymentMethod, cashReceived: 5,
      },
    });
    fixture.destroy();
    await renderPage();

    expect(page.pendingCheckout()).toBe(request);
    expect(host.querySelector('.cart-list')?.textContent).toContain('Synthetic 1');
    expect(host.querySelector('.cart-list')?.textContent).toContain('Bruto $4.00');
    expect(host.querySelector('.summary-lines')).toBeNull();
    expect(host.querySelector('.grand-total')?.textContent?.trim()).toBe('Total no disponible');
    expect(host.querySelector('.summary')?.textContent).not.toContain('$0.00');
    expect(previewSale).not.toHaveBeenCalled();
    const recover = host.querySelector<HTMLButtonElement>('.checkout-zone button')!;
    expect(recover.textContent?.trim()).toBe('Recuperar cobro (F9)');
    const confirm = vi.spyOn(page, 'confirmCheckout').mockImplementation(() => undefined);
    recover.click();
    expect(confirm).toHaveBeenCalledOnce();
    expect(page.checkoutVisible()).toBe(false);
  });

  it('keeps a single persistent recovery notice when its displayed reason changes', () => {
    expect(host.querySelector('.workstation-shell--pending')).toBeNull();
    page.pendingCheckout.set({ requestId: 'SYNTHETIC-PENDING', items: [] });
    fixture.detectChanges();
    expect(host.querySelector('.workstation-shell--pending')).not.toBeNull();
    expect(host.textContent?.match(/Cobro pendiente de recuperar/g)).toHaveLength(1);
    expect(host.textContent).toContain('El resultado no está confirmado');
    page.checkoutPendingMessage.set('Conflicto de intención. Verifica la venta antes de volver a cobrar.');
    fixture.detectChanges();
    expect(host.textContent?.match(/Cobro pendiente de recuperar/g)).toHaveLength(1);
    expect(host.textContent).toContain('Conflicto de intención');
    expect(host.textContent).not.toContain('El resultado no está confirmado');
    expect(Array.from(host.querySelectorAll('button')).filter(button => button.textContent?.trim() === 'Recuperar cobro')).toHaveLength(1);
    page.pendingCheckout.set(null);
    fixture.detectChanges();
    expect(host.querySelector('.workstation-shell--pending')).toBeNull();
  });
});
