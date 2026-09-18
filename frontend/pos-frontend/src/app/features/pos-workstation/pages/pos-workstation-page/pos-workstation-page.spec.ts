import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { CashSession } from '../../../cash-sessions/models/cash-session.model';
import { CashSessionService } from '../../../cash-sessions/services/cash-session.service';
import { CreditNoteService } from '../../../credit-notes/services/credit-note.service';
import { SaleListItem } from '../../models/sale-list-item.model';
import { SalePaymentMethod } from '../../models/sale-payment-method.model';
import { PosKeyboardService } from '../../services/pos-keyboard.service';
import { PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';
import { PosWorkstationPage } from './pos-workstation-page';

describe('PosWorkstationPage void refresh', () => {
  const workstationService = {
    voidSale: vi.fn(() => of({ id: 42, isVoided: true })),
    isBusinessError: vi.fn((error: HttpErrorResponse, code: string) => error.error?.error === code),
    resolveBusinessError: vi.fn(() => 'Debes abrir una caja para registrar la devolución de efectivo.'),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
        { provide: Router, useValue: { navigateByUrl: vi.fn() } },
        { provide: PosProductCatalogService, useValue: {} },
        { provide: PosWorkstationService, useValue: workstationService },
        { provide: CreditNoteService, useValue: {} },
        { provide: CashSessionService, useValue: {} },
        { provide: PosKeyboardService, useValue: {} },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
  });

  it('refreshes sales, cash session and inventory after a successful void', () => {
    const component = TestBed.runInInjectionContext(() => new PosWorkstationPage());
    const loadProducts = vi.spyOn(component, 'loadProducts').mockImplementation(() => undefined);
    const loadCash = vi.spyOn(component, 'loadCurrentCashSession').mockImplementation(() => undefined);
    const loadSales = vi.spyOn(component, 'loadSales').mockImplementation(() => undefined);

    component.saleToVoid.set({
      id: 42,
      number: '001-001-000000042',
      paymentMethod: SalePaymentMethod.Cash,
    } as SaleListItem);
    component.voidVisible.set(true);

    component.confirmVoid('Error de cobro');

    expect(workstationService.voidSale).toHaveBeenCalledWith(42, { reason: 'Error de cobro' });
    expect(loadProducts).toHaveBeenCalledOnce();
    expect(loadCash).toHaveBeenCalledOnce();
    expect(loadSales).toHaveBeenCalledOnce();
    expect(component.voidVisible()).toBe(false);
    expect(component.saleToVoid()).toBeNull();
  });

  it('invalidates and refreshes the current cash session when close wins the race', () => {
    workstationService.voidSale.mockReturnValueOnce(throwError(() => new HttpErrorResponse({
      status: 409,
      error: { error: 'SALE_VOID_CASH_SESSION_REQUIRED' },
    })));
    const component = TestBed.runInInjectionContext(() => new PosWorkstationPage());
    const loadCash = vi.spyOn(component, 'loadCurrentCashSession').mockImplementation(() => undefined);
    component.saleToVoid.set({ id: 42, paymentMethod: SalePaymentMethod.Cash } as SaleListItem);
    component.currentCashSession.set({ id: 9 } as CashSession);

    component.confirmVoid('Error de cobro');

    expect(component.currentCashSession()).toBeNull();
    expect(loadCash).toHaveBeenCalledOnce();
    expect(component.saleToVoid()?.id).toBe(42);
  });
});
