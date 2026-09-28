import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { ProductVatCategory } from '../../../../core/utils/vat-category';
import { Product } from '../../../catalog/models/product.model';
import { ProductService } from '../../../catalog/services/product.service';
import { Supplier } from '../../../suppliers/models/supplier.model';
import { SupplierService } from '../../../suppliers/services/supplier.service';
import { PurchaseReceiptService } from '../../services/purchase-receipt.service';
import { PurchaseReceiptsPage } from './purchase-receipts-page';

describe('PurchaseReceiptsPage remote selectors', () => {
  let supplierGetAll: ReturnType<typeof vi.fn>;
  let supplierLookup: ReturnType<typeof vi.fn>;
  let productGetAll: ReturnType<typeof vi.fn>;
  let productLookup: ReturnType<typeof vi.fn>;
  let receiptGetAll: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    supplierGetAll = vi.fn(() => of([]));
    supplierLookup = vi.fn(() => of([]));
    productGetAll = vi.fn(() => of([]));
    productLookup = vi.fn(() => of([]));
    receiptGetAll = vi.fn(() =>
      of({
        items: [],
        page: 1,
        pageSize: 15,
        totalItems: 0,
        totalPages: 0,
        summary: { postedCount: 0, canceledCount: 0, totalReceived: 0 },
      })
    );

    TestBed.configureTestingModule({
      providers: [
        MessageService,
        {
          provide: PurchaseReceiptService,
          useValue: { getAll: receiptGetAll, getById: vi.fn(), create: vi.fn(), cancel: vi.fn() },
        },
        { provide: SupplierService, useValue: { getAll: supplierGetAll, lookup: supplierLookup } },
        { provide: ProductService, useValue: { getAll: productGetAll, lookup: productLookup } },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: AuthStore, useValue: { companyTimeZoneId: () => 'America/Guayaquil' } },
      ],
    });
  });

  it('does not preload full supplier or product catalogs on page init', () => {
    const page = createPage();

    page.ngOnInit();

    expect(receiptGetAll).toHaveBeenCalledTimes(1);
    expect(supplierGetAll).not.toHaveBeenCalled();
    expect(productGetAll).not.toHaveBeenCalled();
    expect(supplierLookup).not.toHaveBeenCalled();
    expect(productLookup).not.toHaveBeenCalled();
  });

  it('loads bounded supplier and product suggestions only when opening a new receipt', () => {
    const page = createPage();

    page.openCreateDialog();

    expect(supplierLookup).toHaveBeenCalledWith('', 50);
    expect(productLookup).toHaveBeenCalledWith('', 30);
    expect(supplierGetAll).not.toHaveBeenCalled();
    expect(productGetAll).not.toHaveBeenCalled();
  });

  it('keeps the newest product lookup response when an older request finishes later', () => {
    const older = new Subject<Product[]>();
    const newer = new Subject<Product[]>();
    productLookup.mockReturnValueOnce(older).mockReturnValueOnce(newer);
    const page = createPage();

    page.searchProducts('old');
    page.searchProducts('new');
    newer.next([product(2, 'Nuevo', 8)]);
    older.next([product(1, 'Obsoleto', 5)]);

    expect(productLookup).toHaveBeenNthCalledWith(1, 'old', 30);
    expect(productLookup).toHaveBeenNthCalledWith(2, 'new', 30);
    expect(page.products().map((item) => item.name)).toEqual(['Nuevo']);
    expect(page.productLookupLoading()).toBe(false);
  });

  it('keeps the newest supplier lookup response when an older request finishes later', () => {
    const older = new Subject<Supplier[]>();
    const newer = new Subject<Supplier[]>();
    supplierLookup.mockReturnValueOnce(older).mockReturnValueOnce(newer);
    const page = createPage();

    page.searchSuppliers('old');
    page.searchSuppliers('new');
    newer.next([supplier(2, 'Proveedor nuevo')]);
    older.next([supplier(1, 'Proveedor obsoleto')]);

    expect(page.suppliers().map((item) => item.name)).toEqual(['Proveedor nuevo']);
    expect(page.supplierLookupLoading()).toBe(false);
  });

  it('copies the selected product cost into the receipt line and preserves that product across later searches', () => {
    const selected = product(10, 'Seleccionado', 6.75, 'BAR-10', 'INT-10');
    const other = product(20, 'Otro', 2.5);
    productLookup.mockReturnValueOnce(of([other]));
    const page = createPage();
    page.products.set([selected]);
    page.addItem();
    const uid = page.draftItems()[0].uid;

    page.updateItemProduct(uid, selected.id);
    page.searchProducts('otro');

    expect(page.draftItems()[0].productId).toBe(selected.id);
    expect(page.draftItems()[0].unitCost).toBe(6.75);
    expect(page.products().map((item) => item.id)).toEqual([selected.id, other.id]);
    expect(page.productOptions().find((option) => option.value === selected.id)?.label).toContain('BAR-10 / INT-10');
  });

  function createPage(): PurchaseReceiptsPage {
    return TestBed.runInInjectionContext(() => new PurchaseReceiptsPage());
  }
});

function product(
  id: number,
  name: string,
  cost: number,
  barcode: string | null = null,
  internalCode: string | null = null
): Product {
  return {
    id,
    categoryId: 1,
    name,
    barcode,
    internalCode,
    price: cost + 1,
    cost,
    minimumStock: 0,
    vatCategory: ProductVatCategory.Vat15,
    isActive: true,
  };
}

function supplier(id: number, name: string): Supplier {
  return {
    id,
    name,
    identification: `SUP-${id}`,
    email: null,
    phone: null,
    address: null,
    notes: null,
    isActive: true,
    createdAt: '2026-09-28T12:00:00Z',
    updatedAt: null,
  };
}
