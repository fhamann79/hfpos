import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import { PermissionService } from '../../../../core/services/permission.service';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { CashSession, CashSessionStatus } from '../../../cash-sessions/models/cash-session.model';
import { CashSessionService } from '../../../cash-sessions/services/cash-session.service';
import { CreditNoteService } from '../../../credit-notes/services/credit-note.service';
import { SalePreview } from '../../models/checkout-request.model';
import { PosKeyboardService } from '../../services/pos-keyboard.service';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';
import { CheckoutConfirmDialog } from '../../components/checkout-confirm-dialog/checkout-confirm-dialog';
import { CartWorkstation } from '../../components/cart-workstation/cart-workstation';
import { PosWorkstationPage } from './pos-workstation-page';

describe('Authoritative fiscal preview', () => {
  const company = signal(1);
  let responses: Subject<SalePreview>[];
  const api = { previewSale: vi.fn(), createSale: vi.fn(), resolveBusinessError: () => 'Perfil fiscal pendiente' };
  const result = (fingerprint = 'SYNTHETIC-RESOLVED', total = 10): SalePreview => ({
    fingerprint, total, fiscalDate:'2026-10-20',profileRevision:1,grossSubtotal:10,discountAmount:0,subtotal:total,taxAmount:0,
    vat15Subtotal:0,vat5Subtotal:0,vat0Subtotal:total,vatExemptSubtotal:0,vatNotSubjectSubtotal:0,items:[],
  });
  beforeEach(() => {
    sessionStorage.clear(); vi.useFakeTimers(); vi.clearAllMocks(); company.set(1); responses = [];
    api.previewSale.mockImplementation(() => { const response = new Subject<SalePreview>(); responses.push(response); return response; });
    api.createSale.mockReturnValue(new Subject());
    TestBed.configureTestingModule({providers:[
      {provide:PermissionService,useValue:{hasPermission:()=>true}},
      {provide:AuthStore,useValue:{companyId:company,establishmentId:()=>2,emissionPointId:()=>3,me:()=>({userId:'10'}),companyTimeZoneId:()=> 'America/Guayaquil'}},
      {provide:Router,useValue:{}},{provide:MessageService,useValue:{add:vi.fn()}},
      {provide:PosWorkstationService,useValue:api},{provide:PosProductCatalogService,useValue:{}},
      {provide:CashSessionService,useValue:{}},{provide:CreditNoteService,useValue:{}},{provide:PosKeyboardService,useValue:{}},
    ]});
  });
  afterEach(() => { TestBed.resetTestingModule(); vi.useRealTimers(); sessionStorage.clear(); });
  function page() {
    const p = TestBed.runInInjectionContext(() => new PosWorkstationPage());
    vi.spyOn(p,'loadProducts').mockImplementation(()=>undefined); vi.spyOn(p,'loadCurrentCashSession').mockImplementation(()=>undefined); vi.spyOn(p,'loadSales').mockImplementation(()=>undefined);
    p.inventoryAvailable.set(true); p.currentCashSession.set({id:1,status:CashSessionStatus.Open} as CashSession);
    const product={id:7,name:'Synthetic nominal 15',barcode:null,internalCode:'SYN7',price:10,stock:5,isActive:true,vatCategory:ProductVatCategory.Vat15};
    p.cart.set([{productId:7,productName:product.name,product,stock:5,quantity:1,unitPrice:10,discountAmount:0}]); p.cashReceived.set(20);
    return p;
  }
  async function calculate() { TestBed.tick(); await vi.advanceTimersByTimeAsync(160); }

  it('never confirms before preview and uses effective backend zero despite nominal catalog 15', async () => {
    const p=page(); p.confirmCheckout(); expect(api.createSale).not.toHaveBeenCalled();
    await calculate(); responses[0].next(result());
    expect(p.total()).toBe(10); expect(p.taxAmount()).toBe(0); expect(p.fiscalPreviewCurrent()).toBe(true);
    p.confirmCheckout(); expect(api.createSale).toHaveBeenCalledOnce();
    expect(api.createSale.mock.calls[0][0].fiscalFingerprint).toBe('SYNTHETIC-RESOLVED');
    expect(p.cart()[0].product.vatCategory).toBe(ProductVatCategory.Vat15); p.ngOnDestroy();
  });
  it('rejects stale input synchronously and ignores out-of-order calculations', async () => {
    const p=page(); await calculate(); responses[0].next(result());
    p.cart.update(rows=>rows.map(row=>({...row,unitPrice:12})));
    p.confirmCheckout(); expect(api.createSale).not.toHaveBeenCalled();
    await calculate(); responses[0].next(result('OLD',999)); expect(p.fiscalPreview()).toBeNull();
    responses[1].next(result('NEW',12)); expect(p.total()).toBe(12);
    p.confirmCheckout(); expect(api.createSale.mock.calls[0][0].fiscalFingerprint).toBe('NEW'); p.ngOnDestroy();
  });
  it('fails closed on fiscal rejection, tenant change and destroyed pending response', async () => {
    const p=page(); await calculate(); responses[0].error(new HttpErrorResponse({status:409,error:{error:'FISCAL_PROFILE_REQUIRED'}}));
    expect(p.fiscalPreviewError()).toBe('Perfil fiscal pendiente'); p.confirmCheckout(); expect(api.createSale).not.toHaveBeenCalled();
    p.refreshFiscalPreview(); await calculate(); company.set(2); responses[1].next(result());
    expect(p.fiscalPreview()).toBeNull(); await calculate(); p.ngOnDestroy(); responses[2].next(result());
    expect(p.fiscalPreview()).toBeNull();
  });
  it('keeps Escape available but blocks Enter until a calculation is confirmed', () => {
    const dialog=new CheckoutConfirmDialog(); const confirm=vi.spyOn(dialog.confirm,'emit'); const close=vi.spyOn(dialog.visibleChange,'emit');
    dialog.onKeydown(new KeyboardEvent('keydown',{key:'Enter'})); expect(confirm).not.toHaveBeenCalled();
    dialog.onKeydown(new KeyboardEvent('keydown',{key:'Escape'})); expect(close).toHaveBeenCalledWith(false);
    dialog.fiscalReady=true; dialog.onKeydown(new KeyboardEvent('keydown',{key:'Enter'})); expect(confirm).toHaveBeenCalledOnce();
  });

  it('renders effective line tax, allocated discount and total without nominal fallback', () => {
    const p = page(); const cart = new CartWorkstation(); const item = p.cart()[0];
    cart.items = [item]; expect(cart.lineTotal(item)).toBeNull(); expect(cart.vatLabel(item)).toBe('IVA pendiente');
    cart.fiscalPreview = { ...result(), items: [{productId:7,quantity:1,unitPrice:10,discountAmount:1,
      vatCategory:ProductVatCategory.Vat0,vatRate:0,taxableSubtotal:9,taxAmount:0,lineTotal:9}] };
    expect(cart.vatLabel(item)).toBe('IVA 0%'); expect(cart.lineTotal(item)).toBe(9);
    expect(cart.effectiveLine(item)?.discountAmount).toBe(1); expect(item.product.vatCategory).toBe(ProductVatCategory.Vat15);
    cart.fiscalPreview = null; expect(cart.lineTotal(item)).toBeNull(); p.ngOnDestroy();
  });
});
