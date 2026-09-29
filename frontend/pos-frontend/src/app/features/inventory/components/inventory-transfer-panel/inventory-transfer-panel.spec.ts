import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { AuthStore } from '../../../../core/stores/auth.store';
import { InventoryStock, StockStatus } from '../../models/inventory-stock.model';
import { InventoryService } from '../../services/inventory.service';
import { InventoryTransferCreateRequest, InventoryTransferDetail } from '../../models/inventory-transfer.model';
import { InventoryTransferPanel } from './inventory-transfer-panel';

const stock: InventoryStock = {
  productId: 9, productName: 'Mouse', categoryId: 1, categoryName: 'Accesorios',
  quantity: 5, minimumStock: 3, unitCost: 2, inventoryValue: 10,
  stockStatus: StockStatus.Ok, isActive: false,
};
const history = { items: [], page: 1, pageSize: 25, totalItems: 0, totalPages: 0 };
const detail: InventoryTransferDetail = {
  id: 12, requestId: 'test-id', wasAlreadyProcessed: false,
  sourceEstablishmentId: 1, sourceEstablishmentName: 'A',
  destinationEstablishmentId: 2, destinationEstablishmentName: 'B',
  createdByUserId: 4, createdByUsername: 'test', createdAt: '2026-09-29T12:00:00Z',
  businessDate: '2026-09-29', timeZoneIdSnapshot: 'America/Guayaquil',
  reference: null, notes: null, lineCount: 1, totalQuantity: 2, items: [],
};

describe('InventoryTransferPanel', () => {
  function setup(canWrite = true) {
    const inventory = {
      getTransferDestinations: vi.fn(() => of([{ id: 1, name: 'A' }, { id: 2, name: 'B' }])),
      getTransfers: vi.fn(() => of(history)),
      getTransferById: vi.fn(() => of(detail)),
      createTransfer: vi.fn((_payload: InventoryTransferCreateRequest) => of(detail)),
      resolveError: vi.fn(() => 'Stock insuficiente'),
    };
    TestBed.configureTestingModule({ providers: [
      { provide: InventoryService, useValue: inventory },
      { provide: AuthStore, useValue: {
        establishmentId: () => 1, companyTimeZoneId: () => 'America/Guayaquil',
      } },
    ] });
    const panel = TestBed.runInInjectionContext(() => new InventoryTransferPanel());
    panel.canWrite = canWrite;
    panel.stocks = [stock, { ...stock, productId: 10, productName: 'Keyboard' }];
    panel.ngOnInit();
    return { panel, inventory };
  }

  it('hides the transfer form without write permission and excludes the origin', () => {
    const { panel, inventory } = setup(false);
    expect(panel.validationMessage).toContain('permiso');
    panel.submit();
    expect(inventory.createTransfer).not.toHaveBeenCalled();
    expect(inventory.getTransferDestinations).not.toHaveBeenCalled();
    expect(inventory.getTransfers).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 25 }));
  });

  it('rejects invalid destination, empty lines, nonpositive quantity and duplicates', () => {
    const { panel, inventory } = setup();
    expect(panel.destinations()).toEqual([{ id: 2, name: 'B' }]);
    panel.destinationId = 1;
    expect(panel.validationMessage).toContain('destino');
    panel.submit();
    expect(panel.submitError()).toContain('destino');
    expect(inventory.createTransfer).not.toHaveBeenCalled();
    panel.destinationId = 2;
    panel.lines = [];
    expect(panel.validationMessage).toContain('producto');
    panel.lines = [{ id: 1, productId: 9, quantity: 0 }];
    expect(panel.validationMessage).toContain('mayor a cero');
    panel.lines[0].quantity = 6;
    expect(panel.validationMessage).toContain('supera');
    panel.lines[0].quantity = 2;
    panel.addLine();
    panel.lines[1].productId = 9;
    panel.lines[1].quantity = 1;
    expect(panel.validationMessage).toContain('repitas');
    panel.lines[1].productId = 10;
    expect(panel.validationMessage).toBeNull();
    expect(panel.productOptions[0].label).toContain('Inactivo');
  });

  it('retains requestId on retry and rotates it for a new operation', () => {
    const { panel, inventory } = setup();
    panel.destinationId = 2;
    panel.lines = [{ id: 1, productId: 9, quantity: 2 }];
    inventory.createTransfer.mockImplementation(() => throwError(() =>
      new HttpErrorResponse({ status: 409, error: { error: 'INVENTORY_TRANSFER_INSUFFICIENT_STOCK' } })));
    const requestId = panel.requestId;
    panel.submit();
    panel.submit();
    expect(inventory.createTransfer).toHaveBeenCalledTimes(2);
    expect(inventory.createTransfer.mock.calls[0]?.[0]?.requestId).toBe(requestId);
    expect(inventory.createTransfer.mock.calls[1]?.[0]?.requestId).toBe(requestId);
    expect(panel.submitError()).toBe('Stock insuficiente');
    panel.resetDraft();
    expect(panel.requestId).not.toBe(requestId);
  });

  it('refreshes stocks, kardex and paged history after success and exposes detail', () => {
    const { panel, inventory } = setup();
    panel.destinationId = 2;
    panel.lines = [{ id: 1, productId: 9, quantity: 2 }];
    const transferred = vi.fn();
    panel.transferred.subscribe(transferred);
    panel.submit();
    expect(transferred).toHaveBeenCalledOnce();
    expect(inventory.getTransfers).toHaveBeenCalledTimes(2);
    expect(panel.detail()?.id).toBe(12);
    expect(panel.detailVisible).toBe(true);
    panel.loadTransfers(2, 10);
    expect(inventory.getTransfers).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2, pageSize: 10 }));
  });
});
