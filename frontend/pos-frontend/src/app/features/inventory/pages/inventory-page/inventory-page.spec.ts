import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { EMPTY } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { InventoryService } from '../../services/inventory.service';
import { StockStatus } from '../../models/inventory-stock.model';
import { InventoryPage } from './inventory-page';

describe('Inactive product reconciliation', () => {
  const service = {
    registerEntry: vi.fn(() => EMPTY),
    registerExit: vi.fn(() => EMPTY),
    registerAdjustment: vi.fn(() => EMPTY),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ providers: [
      MessageService,
      { provide: InventoryService, useValue: service },
      { provide: PermissionService, useValue: { hasPermission: () => true } },
      { provide: AuthStore, useValue: {} },
    ] });
  });

  it.each(['entry', 'exit', 'adjust'] as const)('allows %s while keeping inactive stock visible', (kind) => {
    const page = TestBed.runInInjectionContext(() => new InventoryPage());
    page.stocks.set([{ productId: 25, productName: 'Mouse', categoryId: 2,
      categoryName: 'Accesorios', quantity: 5, minimumStock: 3, unitCost: 4,
      inventoryValue: 20, stockStatus: StockStatus.Ok, isActive: false }]);
    page.activeOperation = kind;
    page.setOperationProduct(25);
    page.setOperationQuantity(3);
    expect(page.productOptions()[0].label).toBe('25 - Mouse (Inactivo)');
    expect(page.totalInventoryValue()).toBe(20);
    expect(page.canSubmitOperation()).toBe(true);
    page.submitOperation();
    const expected = kind === 'entry' ? service.registerEntry
      : kind === 'exit' ? service.registerExit : service.registerAdjustment;
    expect(expected).toHaveBeenCalledWith(expect.objectContaining({ productId: 25, quantity: 3 }));
    expect(page.stocks()[0].isActive).toBe(false);
  });

  it('continues to reject negative quantities', () => {
    const page = TestBed.runInInjectionContext(() => new InventoryPage());
    page.activeOperation = 'adjust';
    page.setOperationProduct(25);
    page.setOperationQuantity(-1);
    expect(page.canSubmitOperation()).toBe(false);
    page.submitOperation();
    expect(service.registerAdjustment).not.toHaveBeenCalled();
  });
});
