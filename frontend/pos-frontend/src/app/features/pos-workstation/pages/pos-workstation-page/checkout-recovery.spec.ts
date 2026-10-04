import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import { PermissionService } from '../../../../core/services/permission.service';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { CashSession, CashSessionStatus } from '../../../cash-sessions/models/cash-session.model';
import { CashSessionService } from '../../../cash-sessions/services/cash-session.service';
import { CreditNoteService } from '../../../credit-notes/services/credit-note.service';
import { CheckoutRequest } from '../../models/checkout-request.model';
import { Sale } from '../../models/sale.model';
import { SaleDocumentType } from '../../models/sale-document.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { ReceiptPrintService } from '../../services/receipt-print.service';
import { SaleReceiptDialog } from '../../components/sale-receipt-dialog/sale-receipt-dialog';
import { CartWorkstation } from '../../components/cart-workstation/cart-workstation';
import { PosKeyboardService } from '../../services/pos-keyboard.service';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';
import { CheckoutConfirmDialog } from '../../components/checkout-confirm-dialog/checkout-confirm-dialog';
import { PosWorkstationPage } from './pos-workstation-page';

describe('Checkout recovery', () => {
  let response: Subject<Sale>;
  const api = { createSale: vi.fn(), resolveBusinessError: () => 'Synthetic rejection' };
  const catalog = { refreshCartProducts: vi.fn(() => of([])) };
  const context = { companyTimeZoneId: () => 'America/Guayaquil', companyId: () => 1,
    establishmentId: () => 2, emissionPointId: () => 3, username: () => 'synthetic', me: () => ({ userId: '10' }) };
  const product = { id: 7, name: 'Synthetic product', barcode: 'SYN7', internalCode: 'SYN7',
    price: 10, stock: 5, isActive: true, vatCategory: ProductVatCategory.Vat0 };

  beforeEach(() => {
    sessionStorage.clear(); vi.clearAllMocks(); response = new Subject<Sale>(); api.createSale.mockReturnValue(response);
    TestBed.configureTestingModule({ imports: [SaleReceiptDialog, CartWorkstation], providers: [
      { provide: PermissionService, useValue: { hasPermission: (permission: string) => permission !== PERMISSIONS.reportsSalesRead } },
      { provide: AuthStore, useValue: context }, { provide: Router, useValue: { navigateByUrl: vi.fn() } },
      { provide: PosWorkstationService, useValue: api }, { provide: PosProductCatalogService, useValue: catalog },
      { provide: CashSessionService, useValue: {} }, { provide: CreditNoteService, useValue: {} },
      { provide: PosKeyboardService, useValue: { watch: () => new Subject<KeyboardEvent>() } },
      { provide: MessageService, useValue: { add: vi.fn() } },
    ] });
  });

  function page(seedCart = true): PosWorkstationPage {
    const p = TestBed.runInInjectionContext(() => new PosWorkstationPage());
    vi.spyOn(p, 'loadProducts').mockImplementation(() => undefined);
    vi.spyOn(p, 'loadCurrentCashSession').mockImplementation(() => undefined);
    vi.spyOn(p, 'loadSales').mockImplementation(() => undefined);
    p.inventoryAvailable.set(true); p.currentCashSession.set({ id: 1, status: CashSessionStatus.Open } as CashSession);
    if (seedCart) {
      p.cart.set([{ productId: 7, productName: product.name, product, stock: 5, quantity: 1, unitPrice: 10, discountAmount: 0 }]);
      p.cashReceived.set(20);
    }
    return p;
  }
  function sent(): CheckoutRequest { return api.createSale.mock.calls.at(-1)![0] as CheckoutRequest; }
  function success(request: CheckoutRequest): Sale {
    return { id: 42, requestId: request.requestId, total: 10, cashReceived: 20, cashChange: 10,
      number: '001-001-000000042', paymentMethod: SalePaymentMethod.Cash, items: [] } as unknown as Sale;
  }

  it('blocks click/Enter/F12 and material writes synchronously until one response', () => {
    const p = page(); p.checkoutVisible.set(true);
    p.confirmCheckout(); p.confirmCheckout(); p.openCheckoutDialog();
    p.updateQuantity({ productId: 7, quantity: 4 }); p.removeItem(7); p.updateNotes('changed'); p.updateCashReceived(100);
    p.setCheckoutVisible(false);
    expect(api.createSale).toHaveBeenCalledTimes(1); expect(p.checkoutVisible()).toBe(true);
    expect(p.cart()[0].quantity).toBe(1); expect(p.notes()).toBe(''); expect(p.cashReceived()).toBe(20);
    expect(Object.isFrozen(sent().items[0])).toBe(true);
    response.next(success(sent()));
    expect(p.receiptSale()?.cashChange).toBe(10); expect(p.receiptVisible()).toBe(true);
    expect(p.canReadReports).toBe(false);
  });

  it('retries an ambiguous result with identical payload even if current cash/stock changed', () => {
    const p = page(); p.confirmCheckout(); const first = sent();
    response.error(new HttpErrorResponse({ status: 0 }));
    p.currentCashSession.set(null); p.inventoryAvailable.set(false); p.cart.set([]);
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response);
    p.confirmCheckout(); expect(sent()).toBe(first);
    expect(sent().requestId).toBe(first.requestId);
    response.next(success(first)); expect(p.pendingCheckout()).toBeNull(); expect(p.receiptSale()?.id).toBe(42);
  });

  it('retains ambiguous intent across destroy/recreation and ignores destroyed result', () => {
    const p = page(); p.confirmCheckout(); const first = sent(); p.ngOnDestroy();
    response.next(success(first)); expect(p.receiptSale()).toBeNull();
    const recreated = page(false); expect(recreated.cart()).toEqual([]); recreated.ngOnInit(); recreated.confirmCheckout();
    expect(sent()).toEqual(first); expect(sent().requestId).toBe(first.requestId);
    recreated.ngOnDestroy();
  });

  it('does not rotate the request on conflict or 5xx and does not accept malformed success', () => {
    const p = page(); p.confirmCheckout(); const first = sent();
    response.error(new HttpErrorResponse({ status: 409, error: { error: 'REQUEST_CONFLICT' } }));
    p.updateUnitPrice({ productId: 7, unitPrice: 9 });
    expect(p.pendingCheckout()?.requestId).toBe(first.requestId); expect(p.cart()[0].unitPrice).toBe(10);
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response); p.confirmCheckout();
    response.error(new HttpErrorResponse({ status: 503 }));
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response); p.confirmCheckout(); response.next({ id: 42 } as Sale);
    expect(p.pendingCheckout()?.requestId).toBe(first.requestId); expect(p.receiptSale()).toBeNull();
  });

  it('does not send a frozen intent under a changed operational context', () => {
    const p = page(); p.confirmCheckout(); const first = sent(); response.error(new HttpErrorResponse({ status: 0 }));
    const original = context.companyId; context.companyId = () => 99;
    try { p.confirmCheckout(); expect(api.createSale).toHaveBeenCalledTimes(1); expect(p.pendingCheckout()?.requestId).toBe(first.requestId); }
    finally { context.companyId = original; }
  });

  it('discovers pending by immutable actor after point and username change, without cross-actor/tenant leakage', () => {
    const p = page(); p.confirmCheckout(); const first = sent(); response.error(new HttpErrorResponse({ status: 0 })); p.ngOnDestroy();
    const originalPoint = context.emissionPointId; const originalName = context.username;
    const originalActor = context.me; const originalCompany = context.companyId;
    context.emissionPointId = () => 9; context.username = () => 'renamed-synthetic';
    try {
      const moved = page(false); moved.ngOnInit();
      expect(moved.pendingCheckout()?.requestId).toBe(first.requestId); expect(moved.cart()).toEqual([]);
      expect(moved.checkoutRecoveryError()).not.toBe(''); moved.confirmCheckout();
      expect(api.createSale).toHaveBeenCalledTimes(1); moved.ngOnDestroy();
      context.me = () => ({ userId: '11' });
      const otherActor = page(false); otherActor.ngOnInit();
      expect(otherActor.pendingCheckout()).toBeNull(); expect(otherActor.cart()).toEqual([]);
      expect(otherActor.checkoutRecoveryError()).toBe(''); otherActor.ngOnDestroy();
      context.me = originalActor; context.companyId = () => 99;
      const otherTenant = page(false); otherTenant.ngOnInit();
      expect(otherTenant.pendingCheckout()).toBeNull(); expect(otherTenant.cart()).toEqual([]); otherTenant.ngOnDestroy();
      context.companyId = originalCompany; context.emissionPointId = originalPoint;
      response = new Subject<Sale>(); api.createSale.mockReturnValue(response);
      const restored = page(false); restored.ngOnInit(); restored.confirmCheckout();
      expect(sent()).toEqual(first); expect(restored.checkoutRecoveryError()).toBe(''); restored.ngOnDestroy();
    } finally {
      context.emissionPointId = originalPoint; context.username = originalName;
      context.me = originalActor; context.companyId = originalCompany;
    }
  });

  it('restores an empty fresh page draft before releasing a definitive stock rejection', () => {
    const p = page(); p.cart.update(items => [
      { ...items[0], quantity: 2, unitPrice: 11, discountAmount: 1 },
      { ...items[0], productId: 8, productName: 'Other synthetic', product: { ...product, id: 8 }, quantity: 3, unitPrice: 4, discountAmount: 2 },
    ]);
    const originalCart = p.cart();
    const customer = { id: 9, name: 'Synthetic buyer', identificationType: '06', identification: 'SYNBUYER',
      email: 'buyer@hfpos.test', address: 'Synthetic address', isActive: true };
    p.selectedCustomer.set(customer); p.selectedDocumentType.set(SaleDocumentType.Invoice);
    p.saleDiscountAmount.set(2); p.notes.set('Draft notes'); p.cashReceived.set(50); p.confirmCheckout();
    const first = sent(); response.error(new HttpErrorResponse({ status: 0 })); p.ngOnDestroy();
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response);
    const fresh = page(false); expect(fresh.cart()).toEqual([]); expect(fresh.selectedCustomer()).toBeNull(); fresh.ngOnInit();
    fresh.confirmCheckout(); expect(sent()).toEqual(first);
    response.error(new HttpErrorResponse({ status: 409, error: { error: 'INSUFFICIENT_STOCK' } }));
    expect(fresh.cart().map(i => [i.productId, i.quantity, i.unitPrice, i.discountAmount, i.productName]))
      .toEqual(originalCart.map(i => [i.productId, i.quantity, i.unitPrice, i.discountAmount, i.productName]));
    expect(fresh.selectedCustomer()).toEqual(customer); expect(fresh.saleDiscountAmount()).toBe(2);
    expect(fresh.selectedDocumentType()).toBe(SaleDocumentType.Invoice); expect(fresh.notes()).toBe('Draft notes');
    expect(fresh.cashReceived()).toBe(50); expect(fresh.pendingCheckout()).toBeNull(); expect(sessionStorage.length).toBe(0);
    expect(catalog.refreshCartProducts).toHaveBeenCalledWith([7, 8]);
    fresh.updateNotes('Corrected draft'); fresh.cart.update(items => items.map(item => ({ ...item, stock: 5 })));
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response); fresh.confirmCheckout();
    expect(sent().requestId).not.toBe(first.requestId); expect(sent().customerId).toBe(9); expect(sent().items).toHaveLength(2);
    fresh.ngOnDestroy();
  });

  it('validates long notes without truncating and only known 400 notes failure releases a restored draft', async () => {
    const p = page(); p.notes.set('x'.repeat(501)); p.confirmCheckout();
    expect(api.createSale).not.toHaveBeenCalled(); expect(p.notes()).toHaveLength(501);
    const fixture = TestBed.createComponent(CartWorkstation);
    fixture.componentRef.setInput('items', p.cart()); fixture.componentRef.setInput('notes', p.notes());
    fixture.detectChanges(); await fixture.whenStable();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    expect(textarea.maxLength).toBe(500); expect(textarea.value).toHaveLength(501); fixture.destroy();
    p.notes.set('x'.repeat(500)); p.confirmCheckout(); const first = sent();
    response.error(new HttpErrorResponse({ status: 500, error: { error: 'SALE_NOTES_TOO_LONG' } }));
    expect(p.pendingCheckout()?.requestId).toBe(first.requestId); p.ngOnDestroy();
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response);
    const fresh = page(false); fresh.ngOnInit(); fresh.confirmCheckout();
    response.error(new HttpErrorResponse({ status: 400, error: { error: 'SALE_NOTES_TOO_LONG' } }));
    expect(fresh.pendingCheckout()).toBeNull(); expect(fresh.notes()).toHaveLength(500); expect(fresh.cart()).toHaveLength(1);
    fresh.updateNotes('corrected'); response = new Subject<Sale>(); api.createSale.mockReturnValue(response); fresh.confirmCheckout();
    expect(sent().requestId).not.toBe(first.requestId); fresh.ngOnDestroy();
  });

  it('renders and prints B after retained post-sale A, then uses updated A state on reprint', async () => {
    const p = page(); p.confirmCheckout(); response.next(success(sent())); const a = p.receiptSale()!;
    const b = { ...a, id: 43, number: 'SYNTHETIC-B', status: 'Anulada' } as Sale;
    p.openReceipt(b); expect(p.receiptSale()?.id).toBe(42); expect(p.activeReceipt()).toBe(b); expect(p.receiptPostSale()).toBe(false);
    const fixture = TestBed.createComponent(SaleReceiptDialog);
    fixture.componentRef.setInput('sale', p.activeReceipt()); fixture.componentRef.setInput('postSale', p.receiptPostSale());
    fixture.componentRef.setInput('visible', true); fixture.detectChanges(); await fixture.whenStable();
    const print = vi.spyOn(TestBed.inject(ReceiptPrintService), 'print').mockReturnValue({ ok: true, cleanup: vi.fn() });
    fixture.componentInstance.printReceipt();
    expect(print.mock.calls[0][0].textContent).toContain('SYNTHETIC-B'); expect(print.mock.calls[0][0].textContent).not.toContain(a.number);
    const updated = { ...a, status: 'Anulada' } as Sale; p.openReceipt(updated);
    expect(p.activeReceipt()).toBe(updated); p.receiptVisible.set(false); p.openPostSaleReceipt();
    expect(p.activeReceipt()?.status).toBe('Anulada'); expect(p.receiptPostSale()).toBe(true);
    expect(api.createSale).toHaveBeenCalledTimes(1); fixture.destroy();
  });

  it('keeps every cart line/quantity/price/discount after stock rejection and creates a new key for the correction', () => {
    const p = page(); p.cart.update(items => [...items, { ...items[0], productId: 8, product: { ...product, id: 8 }, discountAmount: 1 }]);
    const originalCart = p.cart(); p.cashReceived.set(40); p.confirmCheckout(); const first = sent();
    response.error(new HttpErrorResponse({ status: 409, error: { error: 'INSUFFICIENT_STOCK' } }));
    expect(p.cart().map(i => [i.productId, i.quantity, i.unitPrice, i.discountAmount]))
      .toEqual(originalCart.map(i => [i.productId, i.quantity, i.unitPrice, i.discountAmount]));
    expect(catalog.refreshCartProducts).toHaveBeenCalledWith([7, 8]); expect(p.pendingCheckout()).toBeNull();
    p.removeItem(7); p.cart.update(items => items.map(item => ({ ...item, stock: 5 })));
    response = new Subject<Sale>(); api.createSale.mockReturnValue(response); p.confirmCheckout();
    expect(sent().requestId).not.toBe(first.requestId); expect(sent().items).toHaveLength(1);
  });

  it('validates received and resets the next customer independently of printing', () => {
    const p = page(); p.cashReceived.set(9); p.confirmCheckout(); expect(api.createSale).not.toHaveBeenCalled();
    p.cashReceived.set(20); p.notes.set('Synthetic notes'); p.searchTerm.set('SYN7'); p.confirmCheckout();
    response.next(success(sent())); p.confirmCheckout(); expect(api.createSale).toHaveBeenCalledTimes(1);
    p.nextCustomer(); expect(p.cart()).toEqual([]); expect(p.selectedCustomer()).toBeNull();
    expect(p.notes()).toBe(''); expect(p.searchTerm()).toBe(''); expect(p.cashReceived()).toBeNull();
    expect(p.selectedPaymentMethod()).toBe(SalePaymentMethod.Cash); expect(p.selectedDocumentType()).toBe(SaleDocumentType.Ticket);
    expect(p.receiptSale()).toBeNull(); expect(sessionStorage.length).toBe(0);
    response.next(success(sent())); expect(p.receiptSale()).toBeNull();
  });

  it('does not confirm repeated Enter or Enter in payment selector and blocks Escape while loading', () => {
    const dialog = new CheckoutConfirmDialog(); const confirm = vi.spyOn(dialog.confirm, 'emit');
    const close = vi.spyOn(dialog.visibleChange, 'emit');
    dialog.onKeydown(new KeyboardEvent('keydown', { key: 'Enter', repeat: true }));
    dialog.loading = true; dialog.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(confirm).not.toHaveBeenCalled(); expect(close).not.toHaveBeenCalled();
    dialog.loading = false;
    const select = document.createElement('p-select'); const event = new KeyboardEvent('keydown', { key: 'Enter' });
    Object.defineProperty(event, 'target', { value: select }); dialog.onKeydown(event); expect(confirm).not.toHaveBeenCalled();
    dialog.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' })); expect(confirm).toHaveBeenCalledTimes(1);
  });
});
