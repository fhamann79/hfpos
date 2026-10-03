import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { EMPTY, Observable, Subject, of } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { InventoryService } from '../../services/inventory.service';
import { InventoryStock, InventoryStockPage, StockStatus } from '../../models/inventory-stock.model';
import { InventoryMovement, InventoryMovementSourceType, InventoryMovementType } from '../../models/inventory-movement.model';
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
    page.lookupStocks.set([{ productId: 25, productName: 'Mouse', categoryId: 2,
      categoryName: 'Accesorios', quantity: 5, minimumStock: 3, unitCost: 4,
      inventoryValue: 20, stockStatus: StockStatus.Ok, isActive: false }]);
    page.activeOperation = kind;
    page.setOperationProduct(25);
    page.setOperationQuantity(3);
    expect(page.productOptions()[0].label).toBe('25 - Mouse (Inactivo)');
    expect(page.canSubmitOperation()).toBe(true);
    page.submitOperation();
    const expected = kind === 'entry' ? service.registerEntry
      : kind === 'exit' ? service.registerExit : service.registerAdjustment;
    expect(expected).toHaveBeenCalledWith(expect.objectContaining({ productId: 25, quantity: 3 }));
    expect(page.lookupStocks()[0].isActive).toBe(false);
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

describe('Inventory stock server pagination', () => {
  const stock: InventoryStock = { productId: 25, productName: 'Mouse', categoryId: 2,
    categoryName: 'Accesorios', quantity: 5, minimumStock: 3, unitCost: 4,
    inventoryValue: 20, stockStatus: StockStatus.Ok, isActive: false };
  const result: InventoryStockPage = { items: [stock], page: 1, pageSize: 30,
    totalItems: 95, totalPages: 4, summary: { totalProducts: 95, outOfStockProducts: 20,
      lowStockProducts: 10, inactiveProducts: 3, totalInventoryUnits: 200, totalInventoryValue: 800 } };

  function setup() {
    const service = {
      getStocks: vi.fn((_search: string | null, _id: number | null, _positive: boolean,
        _page: number, _size: number): Observable<InventoryStockPage> => of(result)),
      getMovements: vi.fn(() => EMPTY), resolveError: vi.fn(() => 'Error stock'),
      registerEntry: vi.fn((_payload: unknown): Observable<InventoryMovement> => EMPTY),
      registerExit: vi.fn((_payload: unknown): Observable<InventoryMovement> => EMPTY),
      registerAdjustment: vi.fn((_payload: unknown): Observable<InventoryMovement> => EMPTY),
    };
    TestBed.configureTestingModule({ providers: [MessageService,
      { provide: InventoryService, useValue: service },
      { provide: PermissionService, useValue: { hasPermission: () => true } },
      { provide: AuthStore, useValue: {} },
    ] });
    return { page: TestBed.runInInjectionContext(() => new InventoryPage()), service };
  }

  afterEach(() => vi.useRealTimers());

  it('loads the first bounded page, independent selectors and complete filter totals', () => {
    const { page, service } = setup();
    page.ngOnInit();
    expect(service.getStocks).toHaveBeenCalledWith(null, null, false, 1, 30);
    expect(service.getStocks).toHaveBeenCalledTimes(2);
    expect(page.stocks()).toEqual([stock]);
    expect(page.totalStockItems()).toBe(95);
    expect(page.totalProducts()).toBe(95);
    expect(page.totalInventoryValue()).toBe(800);
    expect(page.outOfStockProducts()).toBe(20);
    expect(page.lowStockProducts()).toBe(10);
    expect(page.inactiveProducts()).toBe(3);
    expect(page.totalInventoryUnits()).toBe(200);
  });

  it('uses lazy page state and resets page on applying or clearing filters', () => {
    const { page, service } = setup();
    service.getStocks.mockReturnValue(of({ ...result, page: 3, pageSize: 10 }));
    page.onStocksLazyLoad({ first: 20, rows: 10 });
    expect(service.getStocks).toHaveBeenLastCalledWith(null, null, false, 3, 10);
    expect(page.stockFirst).toBe(20);
    expect(page.stockRows).toBe(10);
    page.stockSearch = ' mouse ';
    page.stockProductId = 25;
    page.stockOnlyPositive = true;
    service.getStocks.mockReturnValue(of({ ...result, pageSize: 10 }));
    page.applyStockFilters();
    expect(service.getStocks).toHaveBeenLastCalledWith('mouse', 25, true, 1, 10);
    expect(page.stockFirst).toBe(0);
    page.clearStockFilters();
    expect(service.getStocks).toHaveBeenLastCalledWith(null, null, false, 1, 10);
  });

  it('ignores older stock page responses', () => {
    const { page, service } = setup();
    const old = new Subject<InventoryStockPage>();
    const recent = new Subject<InventoryStockPage>();
    service.getStocks.mockReturnValueOnce(old).mockReturnValueOnce(recent);
    page.loadStocks(1, 30);
    page.loadStocks(2, 30);
    recent.next({ ...result, page: 2 });
    old.next({ ...result, items: [] });
    expect(page.stockFirst).toBe(30);
    expect(page.stocks()).toEqual([stock]);
  });

  it('searches independently and retains selected manual and kardex products', () => {
    vi.useFakeTimers();
    const { page, service } = setup();
    page.lookupStocks.set([stock]);
    page.setOperationProduct(stock.productId);
    page.setOperationQuantity(2);
    page.movementProductId = stock.productId;
    page.stocks.set([]);
    service.getStocks.mockReturnValue(of({ ...result, items: [{ ...stock, productId: 33 }] }));
    page.searchProductOptions('key');
    page.searchProductOptions('keyboard');
    vi.advanceTimersByTime(300);
    expect(service.getStocks).toHaveBeenCalledOnce();
    expect(service.getStocks).toHaveBeenLastCalledWith('keyboard', null, false, 1, 30);
    expect(page.selectedOperationStock()?.productId).toBe(25);
    expect(page.focusedProduct()?.productId).toBe(25);
    expect(page.projectedStock()).toBe(7);
    expect(page.canSubmitOperation()).toBe(true);
    page.ngOnDestroy();
  });

  it('rejects stale selector responses and retains focused products from any stock page', () => {
    const { page, service } = setup();
    expect(page.focusedProduct()).toBeNull();
    const old = new Subject<InventoryStockPage>();
    const recent = new Subject<InventoryStockPage>();
    service.getStocks.mockReturnValueOnce(old).mockReturnValueOnce(recent);
    page.loadProductOptions('old');
    page.loadProductOptions('recent');
    recent.next(result);
    recent.complete();
    old.next({ ...result, items: [] });
    old.complete();
    expect(page.lookupStocks()).toEqual([stock]);
    page.movementProductId = 25;
    expect(page.focusedProduct()?.productId).toBe(25);
    page.focusProductMovements({ ...stock, productId: 77 });
    expect(page.focusedProduct()?.productId).toBe(77);
    page.clearProductFocus();
    expect(page.focusedProduct()).toBeNull();
  });

  it.each(['entry', 'exit', 'adjust'] as const)('refreshes selected stock by ID after successful %s', kind => {
    const { page, service } = setup();
    page.lookupStocks.set([stock]);
    page.activeOperation = kind;
    page.setOperationProduct(25);
    page.setOperationQuantity(2);
    page.entryForm.productId = 25;
    page.exitForm.productId = 25;
    page.adjustForm.productId = 25;
    service.getStocks.mockImplementation((_search, id) => of({ ...result,
      items: id === 25 ? [{ ...stock, quantity: 9 }] : [] }));
    const movement: InventoryMovement = { id: 1, productId: 25, productName: 'Mouse',
      type: InventoryMovementType.Entry, sourceType: InventoryMovementSourceType.ManualEntry,
      sourceId: null, sourceLineId: null, quantity: 2, stockBefore: 5, stockAfter: 9,
      reference: null, notes: null, userId: 1, businessDate: '2026-10-02',
      timeZoneIdSnapshot: 'America/Guayaquil', createdAt: '2026-10-02T12:00:00Z' };
    service.registerEntry.mockReturnValue(of(movement));
    service.registerExit.mockReturnValue(of(movement));
    service.registerAdjustment.mockReturnValue(of(movement));
    page.submitOperation();
    expect(page.stockRevision()).toBe(1);
    expect(service.getStocks).toHaveBeenCalledWith(null, 25, false, 1, 1);
    expect(page.focusedProduct()?.quantity).toBe(9);
    page.setOperationProduct(25);
    page.setOperationQuantity(2);
    expect(page.selectedOperationStock()?.quantity).toBe(9);
    expect(page.projectedStock()).toBe(kind === 'entry' ? 11 : kind === 'exit' ? 7 : 2);
  });

  it('refreshes selected off-page products after a transfer without fetching the full list', () => {
    const { page, service } = setup();
    page.lookupStocks.set([stock]);
    page.setOperationProduct(25);
    page.setOperationQuantity(2);
    service.getStocks.mockImplementation((_search, id) => of({ ...result,
      items: id === 25 ? [{ ...stock, quantity: 3 }] : [] }));
    page.onTransferComplete();
    expect(page.stockRevision()).toBe(1);
    expect(page.selectedOperationStock()?.quantity).toBe(3);
    expect(page.projectedStock()).toBe(5);
    expect(service.getStocks.mock.calls.every(call => call[4] <= 30)).toBe(true);
  });
});
