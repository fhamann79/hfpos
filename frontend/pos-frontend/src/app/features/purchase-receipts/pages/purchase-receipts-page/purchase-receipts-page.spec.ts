import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductService } from '../../../catalog/services/product.service';
import { SupplierService } from '../../../suppliers/services/supplier.service';
import {
  PurchaseReceipt,
  PurchaseReceiptStatus,
} from '../../models/purchase-receipt.model';
import { PurchaseReceiptService } from '../../services/purchase-receipt.service';
import { PurchaseReceiptsPage } from './purchase-receipts-page';

describe('Purchase receipt cancellation cost result', () => {
  let fixture: ComponentFixture<PurchaseReceiptsPage>;
  let component: PurchaseReceiptsPage;

  const purchaseReceiptService = {
    getAll: vi.fn(() => of([])),
    getById: vi.fn(),
    create: vi.fn(),
    cancel: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [PurchaseReceiptsPage],
      providers: [
        { provide: PurchaseReceiptService, useValue: purchaseReceiptService },
        { provide: SupplierService, useValue: { getAll: () => of([]) } },
        { provide: ProductService, useValue: { getAll: () => of([]) } },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PurchaseReceiptsPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('explains inventory reversal and provenance-aware cost resolution before canceling', async () => {
    component.selectedReceipt.set(receipt(PurchaseReceiptStatus.Posted, null, null));
    component.openCancelDialog();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text('.warning-callout')).toContain('se revertirá el inventario');
    expect(text('.warning-callout')).toContain('cuando esta recepción continúe siendo su origen');
    expect(text('.warning-callout')).toContain('cualquier costo posterior se conservará');
    expect(text('.warning-callout')).toContain('El historial no se eliminará');
  });

  it('shows the restored cost recorded by the backend for a canceled receipt', async () => {
    showCanceledReceipt(true, 5);

    expect(text('.cost-resolution')).toBe('Restaurado a $5.0000');
  });

  it('shows when the current cost was preserved instead of overwritten', async () => {
    showCanceledReceipt(false, 8);

    expect(text('.cost-resolution')).toBe('Costo vigente preservado: $8.0000');
  });

  function showCanceledReceipt(costChanged: boolean, costAfterCancellation: number): void {
    component.selectedReceipt.set(
      receipt(PurchaseReceiptStatus.Canceled, costChanged, costAfterCancellation)
    );
    component.detailDialogVisible = true;
    fixture.detectChanges();
  }

  function receipt(
    status: PurchaseReceiptStatus,
    costChanged: boolean | null,
    costAfterCancellation: number | null
  ): PurchaseReceipt {
    return {
      id: 40,
      supplierId: 5,
      supplierName: 'Proveedor',
      receiptNumber: 'RC-40',
      supplierDocumentNumber: 'FAC-40',
      receiptDate: '2026-09-18T05:00:00Z',
      receiptBusinessDate: '2026-09-18',
      receiptTimeZoneIdSnapshot: 'America/Guayaquil',
      status,
      subtotal: 6,
      notes: null,
      createdAt: '2026-09-18T13:00:00Z',
      createdByUserId: 1,
      createdByUsername: 'admin',
      postedAt: '2026-09-18T13:00:00Z',
      canceledAt: status === PurchaseReceiptStatus.Canceled ? '2026-09-18T14:00:00Z' : null,
      canceledBusinessDate: status === PurchaseReceiptStatus.Canceled ? '2026-09-18' : null,
      canceledTimeZoneIdSnapshot:
        status === PurchaseReceiptStatus.Canceled ? 'America/Guayaquil' : null,
      canceledByUserId: status === PurchaseReceiptStatus.Canceled ? 1 : null,
      canceledByUsername: status === PurchaseReceiptStatus.Canceled ? 'admin' : null,
      cancelReason: status === PurchaseReceiptStatus.Canceled ? 'Error de recepción' : null,
      items: [
        {
          id: 80,
          productId: 10,
          productName: 'Producto',
          quantity: 1,
          unitCost: 6,
          lineTotal: 6,
          previousProductCost: 5,
          appliedProductCost: 6,
          productCostChangedOnCancellation: costChanged,
          productCostAfterCancellation: costAfterCancellation,
          notes: null,
        },
      ],
    };
  }

  function text(selector: string): string {
    return (fixture.nativeElement as HTMLElement)
      .querySelector(selector)
      ?.textContent?.replace(/\s+/g, ' ')
      .trim() ?? '';
  }
});
