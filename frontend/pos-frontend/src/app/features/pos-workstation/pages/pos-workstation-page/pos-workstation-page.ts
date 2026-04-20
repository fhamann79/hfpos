import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { MessageService } from 'primeng/api';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { Subscription } from 'rxjs';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { CartWorkstation } from '../../components/cart-workstation/cart-workstation';
import { CheckoutConfirmDialog } from '../../components/checkout-confirm-dialog/checkout-confirm-dialog';
import { ProductSearchPanel } from '../../components/product-search-panel/product-search-panel';
import { QuickProductSearchDialog } from '../../components/quick-product-search-dialog/quick-product-search-dialog';
import { RecentSalesPanel } from '../../components/recent-sales-panel/recent-sales-panel';
import { SaleDetailDialog } from '../../components/sale-detail-dialog/sale-detail-dialog';
import { VoidSaleDialog } from '../../components/void-sale-dialog/void-sale-dialog';
import { CartItem } from '../../models/cart-item.model';
import { CheckoutRequest } from '../../models/checkout-request.model';
import { PosProduct } from '../../models/pos-product.model';
import { Sale } from '../../models/sale.model';
import { SaleListItem } from '../../models/sale-list-item.model';
import { PosKeyboardService } from '../../services/pos-keyboard.service';
import { PosCatalogSnapshot, PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';

@Component({
  selector: 'app-pos-workstation-page',
  standalone: true,
  imports: [
    CommonModule,
    MessageModule,
    ToastModule,
    ProductSearchPanel,
    QuickProductSearchDialog,
    CartWorkstation,
    CheckoutConfirmDialog,
    RecentSalesPanel,
    SaleDetailDialog,
    VoidSaleDialog,
  ],
  providers: [MessageService],
  templateUrl: './pos-workstation-page.html',
  styleUrl: './pos-workstation-page.scss',
})
export class PosWorkstationPage implements OnInit, OnDestroy {
  private readonly permissionService = inject(PermissionService);
  private readonly catalogService = inject(PosProductCatalogService);
  private readonly workstationService = inject(PosWorkstationService);
  private readonly keyboard = inject(PosKeyboardService);
  private readonly messageService = inject(MessageService);

  readonly canSell = this.permissionService.hasPermission(PERMISSIONS.posSalesCreate);
  readonly canReadReports = this.permissionService.hasPermission(PERMISSIONS.reportsSalesRead);
  readonly canVoid = this.permissionService.hasPermission(PERMISSIONS.posSalesVoid);

  readonly allProducts = signal<PosProduct[]>([]);
  readonly searchTerm = signal('');
  readonly productsLoading = signal(false);
  readonly productsError = signal('');
  readonly inventoryAvailable = signal(false);
  readonly inventoryError = signal('');

  readonly cart = signal<CartItem[]>([]);
  readonly notes = signal('');
  readonly checkoutVisible = signal(false);
  readonly quickSearchVisible = signal(false);
  readonly checkoutLoading = signal(false);

  readonly sales = signal<SaleListItem[]>([]);
  readonly salesLoading = signal(false);
  readonly salesError = signal('');
  readonly saleDetailVisible = signal(false);
  readonly selectedSale = signal<Sale | null>(null);

  readonly voidVisible = signal(false);
  readonly voidLoading = signal(false);
  readonly saleToVoid = signal<SaleListItem | null>(null);

  readonly filteredProducts = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const products = this.allProducts();

    if (!term.length) {
      return products;
    }

    return products.filter((product) => product.name.toLowerCase().includes(term));
  });

  readonly subtotal = computed(() =>
    this.cart().reduce((sum, item) => sum + item.quantity * item.unitPrice, 0)
  );

  readonly total = computed(() => this.subtotal());

  readonly itemCount = computed(() =>
    this.cart().reduce((count, item) => count + item.quantity, 0)
  );

  private subscriptions: Subscription[] = [];

  ngOnInit(): void {
    if (this.canSell) {
      this.loadProducts();
    }

    if (this.canReadReports) {
      this.loadSales();
    }

    this.subscriptions.push(
      this.keyboard.watch(['F2']).subscribe(() => {
        if (this.canSell) {
          this.quickSearchVisible.set(true);
        }
      }),
      this.keyboard.watch(['F12']).subscribe(() => {
        this.openCheckoutDialog();
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach((subscription) => subscription.unsubscribe());
  }

  loadProducts(): void {
    this.productsLoading.set(true);
    this.productsError.set('');
    this.inventoryError.set('');

    this.catalogService.getProductsWithStock().subscribe({
      next: (snapshot) => {
        this.applyCatalogSnapshot(snapshot);
        this.productsLoading.set(false);
      },
      error: () => {
        this.productsLoading.set(false);
        this.inventoryAvailable.set(false);
        this.productsError.set('No se pudo cargar el catálogo de productos.');
      },
    });
  }

  loadSales(): void {
    this.salesLoading.set(true);
    this.salesError.set('');

    this.workstationService.getSales().subscribe({
      next: (sales) => {
        this.sales.set(sales.sort((a, b) => b.id - a.id));
        this.salesLoading.set(false);
      },
      error: () => {
        this.salesLoading.set(false);
        this.salesError.set('No se pudo cargar el historial de ventas.');
      },
    });
  }

  addProduct(product: PosProduct): void {
    if (!this.canSell) {
      return;
    }

    if (!this.inventoryAvailable()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Inventario no disponible',
        detail: 'No se puede agregar productos mientras el stock no esté disponible.',
      });
      return;
    }

    if (product.stock <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Producto no disponible',
        detail: `"${product.name}" no tiene stock disponible.`,
      });
      return;
    }

    this.cart.update((items) => {
      const found = items.find((item) => item.productId === product.id);

      if (found) {
        if (found.quantity >= found.stock) {
          this.notifyStockLimit(found.productName, found.stock);
          return items;
        }

        return items.map((item) =>
          item.productId === product.id ? { ...item, quantity: item.quantity + 1 } : item
        );
      }

      return [
        ...items,
        {
          productId: product.id,
          productName: product.name,
          quantity: 1,
          unitPrice: product.price,
          stock: product.stock,
          product,
        },
      ];
    });
  }

  updateQuantity(event: { productId: number; quantity: number }): void {
    let limitedItemName: string | null = null;
    let limitedStock = 0;

    this.cart.update((items) =>
      items.map((item) => {
        if (item.productId !== event.productId) {
          return item;
        }

        const requestedQuantity = Math.max(1, Math.floor(event.quantity || 1));
        const maxQuantity = this.inventoryAvailable() ? Math.max(item.stock, 1) : requestedQuantity;
        const nextQuantity = Math.min(requestedQuantity, maxQuantity);

        if (this.inventoryAvailable() && nextQuantity !== requestedQuantity) {
          limitedItemName = item.productName;
          limitedStock = item.stock;
        }

        return { ...item, quantity: nextQuantity };
      })
    );

    if (limitedItemName !== null) {
      this.notifyStockLimit(limitedItemName, limitedStock);
    }
  }

  updateUnitPrice(event: { productId: number; unitPrice: number }): void {
    this.cart.update((items) =>
      items.map((item) =>
        item.productId === event.productId
          ? { ...item, unitPrice: Math.max(0, Number(event.unitPrice || 0)) }
          : item
      )
    );
  }

  removeItem(productId: number): void {
    this.cart.update((items) => items.filter((item) => item.productId !== productId));
  }

  openCheckoutDialog(): void {
    if (!this.canSell || !this.cart().length) {
      return;
    }

    if (!this.inventoryAvailable()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Error al consultar inventario',
        detail: 'No se puede confirmar la venta sin stock actualizado.',
      });
      return;
    }

    const invalidItems = this.findCartItemsExceedingStock();
    if (invalidItems.length > 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Carrito inconsistente',
        detail: this.buildStockValidationMessage(invalidItems),
      });
      this.reconcileCartWithCatalog();
      return;
    }

    this.checkoutVisible.set(true);
  }

  confirmCheckout(): void {
    if (!this.canSell || !this.cart().length) {
      return;
    }

    if (!this.inventoryAvailable()) {
      this.messageService.add({
        severity: 'error',
        summary: 'Error al consultar inventario',
        detail: 'No se puede enviar la venta sin stock actualizado.',
      });
      return;
    }

    const invalidItems = this.findCartItemsExceedingStock();
    if (invalidItems.length > 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Carrito inconsistente',
        detail: this.buildStockValidationMessage(invalidItems),
      });
      this.checkoutVisible.set(false);
      this.reconcileCartWithCatalog();
      return;
    }

    const payload: CheckoutRequest = {
      notes: this.notes().trim() || undefined,
      items: this.cart().map((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
      })),
    };

    this.checkoutLoading.set(true);

    this.workstationService.createSale(payload).subscribe({
      next: () => {
        this.checkoutLoading.set(false);
        this.checkoutVisible.set(false);
        this.cart.set([]);
        this.notes.set('');
        this.messageService.add({ severity: 'success', summary: 'Venta registrada', detail: 'La venta fue creada correctamente.' });
        this.refreshOperationalData();
      },
      error: (error: HttpErrorResponse) => {
        this.checkoutLoading.set(false);
        this.checkoutVisible.set(false);

        if (this.workstationService.isBusinessError(error, 'INSUFFICIENT_STOCK')) {
          this.messageService.add({
            severity: 'warn',
            summary: 'Stock actualizado',
            detail:
              'El stock cambió mientras preparabas la venta. Se refrescará el POS para reconciliar el carrito.',
          });
        } else {
          this.messageService.add({
            severity: 'error',
            summary: 'No se pudo completar la venta',
            detail: this.workstationService.resolveBusinessError(error),
          });
        }

        this.refreshOperationalData();
      },
    });
  }

  openSaleDetail(saleId: number): void {
    this.saleDetailVisible.set(true);
    this.selectedSale.set(null);

    this.workstationService.getSaleDetail(saleId).subscribe({
      next: (sale) => this.selectedSale.set(sale),
      error: () => {
        this.saleDetailVisible.set(false);
        this.messageService.add({ severity: 'error', summary: 'Detalle', detail: 'No se pudo obtener el detalle de la venta.' });
      },
    });
  }

  openVoidDialog(sale: SaleListItem): void {
    this.saleToVoid.set(sale);
    this.voidVisible.set(true);
  }

  confirmVoid(reason: string): void {
    const sale = this.saleToVoid();
    if (!sale) {
      return;
    }

    this.voidLoading.set(true);
    this.workstationService.voidSale(sale.id, { reason }).subscribe({
      next: () => {
        this.voidLoading.set(false);
        this.voidVisible.set(false);
        this.saleToVoid.set(null);
        this.messageService.add({ severity: 'success', summary: 'Venta anulada', detail: 'La venta fue anulada correctamente.' });
        this.refreshOperationalData();
        if (this.selectedSale()?.id === sale.id) {
          this.openSaleDetail(sale.id);
        }
      },
      error: (error: HttpErrorResponse) => {
        this.voidLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'No se pudo anular',
          detail: this.workstationService.resolveBusinessError(error),
        });
      },
    });
  }

  handleUnavailableProductSelection(product: PosProduct): void {
    if (!this.inventoryAvailable()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Inventario no disponible',
        detail: 'No se puede agregar productos mientras el stock no esté disponible.',
      });
      return;
    }

    this.messageService.add({
      severity: 'warn',
      summary: 'Producto no disponible',
      detail: `"${product.name}" no tiene stock disponible.`,
    });
  }

  private applyCatalogSnapshot(snapshot: PosCatalogSnapshot): void {
    this.inventoryAvailable.set(snapshot.inventoryAvailable);
    this.allProducts.set(snapshot.products);

    if (snapshot.inventoryAvailable) {
      this.inventoryError.set('');
      this.reconcileCartWithCatalog();
      return;
    }

    this.inventoryError.set('No se pudo cargar el stock. Intenta refrescar antes de vender.');
  }

  private refreshOperationalData(): void {
    if (this.canSell) {
      this.loadProducts();
    }

    if (this.canReadReports) {
      this.loadSales();
    }
  }

  private reconcileCartWithCatalog(): void {
    const stockMap = new Map(this.allProducts().map((product) => [product.id, product]));

    this.cart.update((items) =>
      items
        .map((item) => {
          const product = stockMap.get(item.productId);

          if (!product) {
            return null;
          }

          const nextStock = this.inventoryAvailable() ? product.stock : item.stock;
          const nextQuantity = this.inventoryAvailable()
            ? Math.min(item.quantity, Math.max(product.stock, 0))
            : item.quantity;

          if (this.inventoryAvailable() && nextQuantity <= 0) {
            return null;
          }

          return {
            ...item,
            productName: product.name,
            unitPrice: item.unitPrice,
            stock: nextStock,
            quantity: nextQuantity,
            product,
          };
        })
        .filter((item): item is CartItem => !!item)
    );
  }

  private findCartItemsExceedingStock(): CartItem[] {
    if (!this.inventoryAvailable()) {
      return [];
    }

    return this.cart().filter((item) => item.quantity > item.stock);
  }

  private buildStockValidationMessage(items: CartItem[]): string {
    const [firstItem] = items;
    if (!firstItem) {
      return 'Hay productos con cantidades mayores al stock disponible.';
    }

    if (items.length === 1) {
      return `"${firstItem.productName}" supera el stock disponible (${firstItem.stock}).`;
    }

    return 'Hay varios productos con cantidades mayores al stock disponible.';
  }

  private notifyStockLimit(productName: string, stock: number): void {
    this.messageService.add({
      severity: 'warn',
      summary: 'Stock máximo alcanzado',
      detail: `"${productName}" solo tiene ${stock} unidades disponibles.`,
    });
  }
}
