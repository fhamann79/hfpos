import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { ProductVatCategory } from '../../../core/utils/vat-category';
import { PosProduct } from '../models/pos-product.model';
import { PosProductCatalogService } from '../services/pos-product-catalog.service';
import { ProductSearchPanel } from './product-search-panel/product-search-panel';
import { QuickProductSearchDialog } from './quick-product-search-dialog/quick-product-search-dialog';

describe('POS remote product search', () => {
  const catalogService = {
    searchProducts: vi.fn(),
  };

  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    catalogService.searchProducts.mockReturnValue(of([]));
    TestBed.configureTestingModule({
      providers: [{ provide: PosProductCatalogService, useValue: catalogService }],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('main search sends the latest term remotely and ignores an older response', () => {
    const older = new Subject<PosProduct[]>();
    const newer = new Subject<PosProduct[]>();
    catalogService.searchProducts.mockReturnValueOnce(older).mockReturnValueOnce(newer);
    const panel = createPanel();

    panel.searchTerm = 'old';
    panel.onSearchChange('old');
    vi.advanceTimersByTime(150);

    panel.searchTerm = 'new';
    panel.onSearchChange('new');
    vi.advanceTimersByTime(150);

    newer.next([product(2, 'Nuevo', 8)]);
    older.next([product(1, 'Obsoleto', 5)]);

    expect(catalogService.searchProducts).toHaveBeenNthCalledWith(1, 'old', 30);
    expect(catalogService.searchProducts).toHaveBeenNthCalledWith(2, 'new', 30);
    expect(panel.results.map((item) => item.id)).toEqual([2]);
  });

  it('clears previous main-search results before the next debounce so Enter cannot add a stale product', () => {
    const previous = product(3, 'Anterior', 4);
    const pending = new Subject<PosProduct[]>();
    catalogService.searchProducts.mockReturnValueOnce(of([previous])).mockReturnValueOnce(pending);
    const panel = createPanel();
    panel.canSell = true;
    panel.inventoryAvailable = true;
    const added = vi.fn();
    panel.addProduct.subscribe(added);

    panel.searchTerm = 'anterior';
    panel.onSearchChange('anterior');
    vi.advanceTimersByTime(150);
    expect(panel.results.map((item) => item.id)).toEqual([3]);

    panel.searchTerm = 'nuevo';
    panel.onSearchChange('nuevo');
    expect(panel.results).toEqual([]);

    panel.onSearchKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(added).not.toHaveBeenCalled();
  });

  it('exact barcode response keeps scanner behavior and emits the stock snapshot', () => {
    const scanned = product(7, 'Escaneado', 6, 'BAR-7', 'INT-7');
    catalogService.searchProducts.mockReturnValue(of([scanned]));
    const panel = createPanel();
    panel.canSell = true;
    panel.inventoryAvailable = true;
    const added = vi.fn();
    const searchChanges = vi.fn();
    panel.addProduct.subscribe(added);
    panel.searchTermChange.subscribe(searchChanges);

    panel.searchTerm = 'BAR-7';
    panel.onSearchChange('BAR-7');
    vi.advanceTimersByTime(150);

    expect(added).toHaveBeenCalledWith(scanned);
    expect(added.mock.calls[0][0]).toEqual(expect.objectContaining({ price: 6, stock: 8 }));
    expect(searchChanges).toHaveBeenLastCalledWith('');
  });

  it('F2 loads bounded initial results and uses remote searches while preserving Enter selection', () => {
    const initial = product(10, 'Inicial', 2);
    const found = product(11, 'Encontrado', 3, null, 'F2-11');
    catalogService.searchProducts.mockReturnValueOnce(of([initial])).mockReturnValueOnce(of([found]));
    const dialog = createDialog();
    dialog.visible = true;
    dialog.inventoryAvailable = true;
    const selected = vi.fn();
    dialog.selectProduct.subscribe(selected);

    dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });
    expect(catalogService.searchProducts).toHaveBeenNthCalledWith(1, '', 30);
    expect(dialog.filteredProducts.map((item) => item.id)).toEqual([10]);

    dialog.onFilterChange('F2-11');
    vi.advanceTimersByTime(150);
    expect(catalogService.searchProducts).toHaveBeenNthCalledWith(2, 'F2-11', 30);
    expect(dialog.filteredProducts.map((item) => item.id)).toEqual([11]);

    dialog.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(selected).toHaveBeenCalledWith(found);
  });

  it('clears previous F2 results before the next debounce so Enter cannot select a stale product', () => {
    const previous = product(12, 'Anterior F2', 2);
    const pending = new Subject<PosProduct[]>();
    catalogService.searchProducts.mockReturnValueOnce(of([previous])).mockReturnValueOnce(pending);
    const dialog = createDialog();
    dialog.visible = true;
    dialog.inventoryAvailable = true;
    const selected = vi.fn();
    dialog.selectProduct.subscribe(selected);

    dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });
    expect(dialog.filteredProducts.map((item) => item.id)).toEqual([12]);

    dialog.onFilterChange('nuevo');
    expect(dialog.filteredProducts).toEqual([]);

    dialog.onKeydown(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(selected).not.toHaveBeenCalled();
  });

  it('F2 ignores stale responses after a newer remote lookup', () => {
    catalogService.searchProducts.mockReturnValueOnce(of([]));
    const older = new Subject<PosProduct[]>();
    const newer = new Subject<PosProduct[]>();
    catalogService.searchProducts.mockReturnValueOnce(older).mockReturnValueOnce(newer);
    const dialog = createDialog();
    dialog.visible = true;
    dialog.ngOnChanges({ visible: new SimpleChange(false, true, true) });

    dialog.onFilterChange('old');
    vi.advanceTimersByTime(150);
    dialog.onFilterChange('new');
    vi.advanceTimersByTime(150);

    newer.next([product(20, 'Nuevo F2', 1)]);
    older.next([product(19, 'Viejo F2', 1)]);

    expect(dialog.filteredProducts.map((item) => item.id)).toEqual([20]);
  });

  function createPanel(): ProductSearchPanel {
    return TestBed.runInInjectionContext(() => new ProductSearchPanel());
  }

  function createDialog(): QuickProductSearchDialog {
    return TestBed.runInInjectionContext(() => new QuickProductSearchDialog());
  }
});

function product(
  id: number,
  name: string,
  price: number,
  barcode: string | null = null,
  internalCode: string | null = null
): PosProduct {
  return {
    id,
    name,
    barcode,
    internalCode,
    price,
    vatCategory: ProductVatCategory.Vat15,
    isActive: true,
    stock: 8,
  };
}
