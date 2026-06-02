import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { ToastModule } from 'primeng/toast';
import { EMPTY, Observable, Subscription, finalize, fromEvent, of, switchMap, tap } from 'rxjs';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { calculateTaxSummary, roundMoney } from '../../../../core/utils/vat-category';
import { CartWorkstation } from '../../components/cart-workstation/cart-workstation';
import { CheckoutConfirmDialog } from '../../components/checkout-confirm-dialog/checkout-confirm-dialog';
import { CustomerSelectorDialog } from '../../components/customer-selector-dialog/customer-selector-dialog';
import { ProductSearchPanel } from '../../components/product-search-panel/product-search-panel';
import { QuickProductSearchDialog } from '../../components/quick-product-search-dialog/quick-product-search-dialog';
import { RecentSalesPanel } from '../../components/recent-sales-panel/recent-sales-panel';
import { SaleDetailDialog } from '../../components/sale-detail-dialog/sale-detail-dialog';
import { SriSubmissionAttemptsDialog } from '../../components/sri-submission-attempts-dialog/sri-submission-attempts-dialog';
import { VoidSaleDialog } from '../../components/void-sale-dialog/void-sale-dialog';
import { CartItem } from '../../models/cart-item.model';
import { CheckoutRequest } from '../../models/checkout-request.model';
import { PosCustomer } from '../../models/pos-customer.model';
import { PosProduct } from '../../models/pos-product.model';
import { SaleDocumentStatus, SaleDocumentType } from '../../models/sale-document.model';
import { Sale } from '../../models/sale.model';
import { SaleListItem } from '../../models/sale-list-item.model';
import { SriSubmissionAttempt } from '../../models/sri-submission-attempt.model';
import { PosKeyboardService } from '../../services/pos-keyboard.service';
import { PosCatalogSnapshot, PosProductCatalogService } from '../../services/pos-product-catalog.service';
import { PosWorkstationService } from '../../services/pos-workstation.service';

@Component({
  selector: 'app-pos-workstation-page',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    MessageModule,
    ToastModule,
    ProductSearchPanel,
    QuickProductSearchDialog,
    CartWorkstation,
    CheckoutConfirmDialog,
    CustomerSelectorDialog,
    RecentSalesPanel,
    SaleDetailDialog,
    SriSubmissionAttemptsDialog,
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

  @ViewChild(ProductSearchPanel) private productSearchPanel?: ProductSearchPanel;

  readonly canSell = this.permissionService.hasPermission(PERMISSIONS.posSalesCreate);
  readonly canReadReports = this.permissionService.hasPermission(PERMISSIONS.reportsSalesRead);
  readonly canVoid = this.permissionService.hasPermission(PERMISSIONS.posSalesVoid);
  readonly canSignSriDocuments = this.permissionService.hasPermission(PERMISSIONS.sriDocumentsSign);
  readonly canSubmitSriDocuments = this.permissionService.hasPermission(PERMISSIONS.sriDocumentsSubmit);

  readonly allProducts = signal<PosProduct[]>([]);
  readonly searchTerm = signal('');
  readonly productsLoading = signal(false);
  readonly productsError = signal('');
  readonly inventoryAvailable = signal(false);
  readonly inventoryError = signal('');

  readonly cart = signal<CartItem[]>([]);
  readonly activeCartProductId = signal<number | null>(null);
  readonly selectedCustomer = signal<PosCustomer | null>(null);
  readonly saleDiscountAmount = signal(0);
  readonly selectedDocumentType = signal<SaleDocumentType>(SaleDocumentType.Ticket);
  readonly notes = signal('');
  readonly checkoutVisible = signal(false);
  readonly customerSelectorVisible = signal(false);
  readonly quickSearchVisible = signal(false);
  readonly recentSalesVisible = signal(false);
  readonly checkoutLoading = signal(false);

  readonly sales = signal<SaleListItem[]>([]);
  readonly salesLoading = signal(false);
  readonly salesError = signal('');
  readonly saleDetailVisible = signal(false);
  readonly selectedSale = signal<Sale | null>(null);
  readonly sriSigningSaleId = signal<number | null>(null);
  readonly sriSubmittingSaleId = signal<number | null>(null);
  readonly sriCheckingAuthorizationSaleId = signal<number | null>(null);
  readonly sriProcessingSaleId = signal<number | null>(null);
  readonly sriAttemptsVisible = signal(false);
  readonly sriAttemptsLoading = signal(false);
  readonly sriAttemptsError = signal('');
  readonly sriAttempts = signal<SriSubmissionAttempt[]>([]);
  readonly sriAttemptsSale = signal<Sale | null>(null);

  readonly voidVisible = signal(false);
  readonly voidLoading = signal(false);
  readonly saleToVoid = signal<SaleListItem | null>(null);

  readonly filteredProducts = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const products = this.allProducts();

    if (!term.length) {
      return [];
    }

    return products
      .filter((product) => this.productMatchesTerm(product, term))
      .sort((a, b) => this.productMatchRank(a, term) - this.productMatchRank(b, term) || a.name.localeCompare(b.name));
  });

  readonly maxSaleDiscountAmount = computed(() =>
    this.cart().reduce((sum, item) => sum + Math.max(item.quantity * item.unitPrice - item.discountAmount, 0), 0)
  );

  readonly effectiveSaleDiscountAmount = computed(() =>
    roundMoney(Math.min(Math.max(this.saleDiscountAmount(), 0), this.maxSaleDiscountAmount()))
  );

  readonly taxSummary = computed(() => calculateTaxSummary(this.cart(), this.effectiveSaleDiscountAmount()));

  readonly subtotal = computed(() => this.taxSummary().subtotal);

  readonly taxAmount = computed(() => this.taxSummary().taxAmount);

  readonly total = computed(() => this.taxSummary().total);

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
      this.keyboard.watch(['F9']).subscribe(() => {
        this.openCheckoutDialog();
      }),
      this.keyboard.watch(['F8']).subscribe(() => {
        if (this.canReadReports) {
          this.recentSalesVisible.set(true);
        }
      }),
      this.keyboard.watch(['F4']).subscribe(() => {
        if (this.canSell) {
          this.customerSelectorVisible.set(true);
        }
      }),
      this.keyboard.watch(['F12']).subscribe(() => {
        if (this.checkoutVisible()) {
          this.confirmCheckout();
          return;
        }

        this.openCheckoutDialog();
      }),
      this.keyboard.watch(['Escape']).subscribe(() => {
        this.closeContextualDialog();
      }),
      fromEvent<KeyboardEvent>(window, 'keydown').subscribe((event) => {
        this.handleCartKeyboardShortcut(event);
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

  onSearchTermChange(value: string): void {
    this.searchTerm.set(value);

    const product = this.findExactIdentifierMatch(value);
    if (!product) {
      return;
    }

    if (this.addProduct(product)) {
      this.clearSearchAndFocus();
    }
  }

  submitSearchTerm(): void {
    const term = this.searchTerm().trim();
    if (!term.length) {
      this.focusMainSearch();
      return;
    }

    const exactProduct = this.findExactIdentifierMatch(term);
    if (exactProduct) {
      if (this.addProduct(exactProduct)) {
        this.clearSearchAndFocus();
      }
      return;
    }

    const matches = this.filteredProducts();
    if (matches.length === 1 && this.addProduct(matches[0])) {
      this.clearSearchAndFocus();
      return;
    }

    this.focusMainSearch();
  }

  addProduct(product: PosProduct): boolean {
    if (!this.canSell) {
      return false;
    }

    if (!this.inventoryAvailable()) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Inventario no disponible',
        detail: 'No se puede agregar productos mientras el stock no esté disponible.',
      });
      return false;
    }

    if (product.stock <= 0) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Producto no disponible',
        detail: `"${product.name}" no tiene stock disponible.`,
      });
      return false;
    }

    let productAdded = false;

    this.cart.update((items) => {
      const found = items.find((item) => item.productId === product.id);

      if (found) {
        if (found.quantity >= found.stock) {
          this.notifyStockLimit(found.productName, found.stock);
          return items;
        }

        productAdded = true;
        return items.map((item) =>
          item.productId === product.id ? { ...item, quantity: item.quantity + 1 } : item
        );
      }

      productAdded = true;
      return [
        ...items,
        {
          productId: product.id,
          productName: product.name,
          quantity: 1,
          unitPrice: product.price,
          discountAmount: 0,
          stock: product.stock,
          product,
        },
      ];
    });

    if (productAdded) {
      this.activeCartProductId.set(product.id);
    }

    return productAdded;
  }

  updateQuantity(event: { productId: number; quantity: number }): void {
    this.activeCartProductId.set(event.productId);

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

        return {
          ...item,
          quantity: nextQuantity,
          discountAmount: this.normalizeDiscount(item.discountAmount, nextQuantity * item.unitPrice),
        };
      })
    );

    if (limitedItemName !== null) {
      this.notifyStockLimit(limitedItemName, limitedStock);
    }

    this.saleDiscountAmount.set(this.normalizeDiscount(this.saleDiscountAmount(), this.maxSaleDiscountAmount()));
  }

  updateUnitPrice(event: { productId: number; unitPrice: number }): void {
    this.activeCartProductId.set(event.productId);

    this.cart.update((items) =>
      items.map((item) =>
        item.productId === event.productId
          ? {
              ...item,
              unitPrice: Math.max(0, Number(event.unitPrice || 0)),
              discountAmount: this.normalizeDiscount(item.discountAmount, item.quantity * Math.max(0, Number(event.unitPrice || 0))),
            }
          : item
      )
    );
    this.saleDiscountAmount.set(this.normalizeDiscount(this.saleDiscountAmount(), this.maxSaleDiscountAmount()));
  }

  updateLineDiscount(event: { productId: number; discountAmount: number }): void {
    this.activeCartProductId.set(event.productId);

    this.cart.update((items) =>
      items.map((item) =>
        item.productId === event.productId
          ? { ...item, discountAmount: this.normalizeDiscount(event.discountAmount, item.quantity * item.unitPrice) }
          : item
      )
    );
    this.saleDiscountAmount.set(this.normalizeDiscount(this.saleDiscountAmount(), this.maxSaleDiscountAmount()));
  }

  updateSaleDiscount(discountAmount: number): void {
    this.saleDiscountAmount.set(this.normalizeDiscount(discountAmount, this.maxSaleDiscountAmount()));
  }

  removeItem(productId: number): void {
    const items = this.cart();
    const removedIndex = items.findIndex((item) => item.productId === productId);

    this.cart.update((items) => items.filter((item) => item.productId !== productId));
    this.moveActiveLineAfterRemoval(removedIndex);
    this.saleDiscountAmount.set(this.normalizeDiscount(this.saleDiscountAmount(), this.maxSaleDiscountAmount()));
  }

  selectCartLine(productId: number): void {
    if (this.cart().some((item) => item.productId === productId)) {
      this.activeCartProductId.set(productId);
    }
  }

  openCustomerSelector(): void {
    if (!this.canSell) {
      return;
    }

    this.customerSelectorVisible.set(true);
  }

  onCustomerSelectorVisibleChange(visible: boolean): void {
    this.customerSelectorVisible.set(visible);

    if (!visible) {
      this.focusMainSearch();
    }
  }

  selectCustomer(customer: PosCustomer): void {
    this.selectedCustomer.set(customer);
    this.customerSelectorVisible.set(false);
    this.focusMainSearch();
  }

  clearCustomer(): void {
    this.selectedCustomer.set(null);
    this.focusMainSearch();
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
      customerId: this.selectedCustomer()?.id ?? null,
      documentType: this.selectedDocumentType(),
      discountAmount: this.effectiveSaleDiscountAmount(),
      notes: this.notes().trim() || undefined,
      items: this.cart().map((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
        discountAmount: item.discountAmount,
      })),
    };

    this.checkoutLoading.set(true);

    this.workstationService.createSale(payload).subscribe({
      next: () => {
        this.checkoutLoading.set(false);
        this.checkoutVisible.set(false);
        this.cart.set([]);
        this.activeCartProductId.set(null);
        this.selectedCustomer.set(null);
        this.saleDiscountAmount.set(0);
        this.selectedDocumentType.set(SaleDocumentType.Ticket);
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

  processSriWorkflow(saleId: number): void {
    if (!this.canSignSriDocuments || !this.canSubmitSriDocuments) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Permisos SRI',
        detail: 'No tienes permisos suficientes para procesar SRI.',
      });
      return;
    }

    if (!saleId || this.hasActiveSriProcessing()) {
      return;
    }

    this.sriProcessingSaleId.set(saleId);

    const selectedSale = this.selectedSale()?.id === saleId ? this.selectedSale() : null;
    const sale$ = selectedSale ? of(selectedSale) : this.workstationService.getSaleDetail(saleId);

    sale$.pipe(
      switchMap((sale) => {
        const validationMessage = this.getSriWorkflowValidationMessage(sale);

        if (validationMessage) {
          this.messageService.add({
            severity: 'warn',
            summary: 'Procesamiento SRI',
            detail: validationMessage,
          });
          return EMPTY;
        }

        return this.runSriWorkflow(sale);
      }),
      finalize(() => {
        this.sriProcessingSaleId.set(null);
        this.refreshSriWorkflowContext(saleId);
      })
    ).subscribe({
      next: (sale) => this.handleSriWorkflowSuccess(sale),
      error: (error: unknown) => this.handleSriWorkflowError(error),
    });
  }

  signSriXml(saleId: number): void {
    if (!this.canSignSriDocuments || this.sriSigningSaleId() || this.sriProcessingSaleId()) {
      return;
    }

    this.sriSigningSaleId.set(saleId);

    this.workstationService.signInvoiceXml(saleId).subscribe({
      next: (sale) => {
        this.sriSigningSaleId.set(null);
        this.selectedSale.set(sale);
        this.loadSales();
        this.messageService.add({
          severity: 'success',
          summary: 'XML firmado',
          detail: 'XML firmado correctamente. La factura aún no ha sido autorizada por el SRI.',
        });
      },
      error: (error: HttpErrorResponse) => {
        this.sriSigningSaleId.set(null);
        this.messageService.add({
          severity: 'error',
          summary: 'No se pudo firmar',
          detail: this.workstationService.resolveBusinessError(error),
        });
      },
    });
  }

  submitSriInvoice(saleId: number): void {
    if (!this.canSubmitSriDocuments || this.sriSubmittingSaleId() || this.sriProcessingSaleId()) {
      return;
    }

    this.sriSubmittingSaleId.set(saleId);

    this.workstationService.submitSriInvoice(saleId).subscribe({
      next: (sale) => {
        this.sriSubmittingSaleId.set(null);
        this.selectedSale.set(sale);
        this.loadSales();
        this.reloadSriAttemptsIfOpen(saleId);
        this.messageService.add({
          severity: 'success',
          summary: 'Recibido por SRI',
          detail: 'Comprobante recibido por SRI. Consulta la autorización.',
        });
      },
      error: (error: HttpErrorResponse) => {
        this.sriSubmittingSaleId.set(null);
        this.refreshSelectedSale(saleId);
        this.reloadSriAttemptsIfOpen(saleId);
        this.messageService.add({
          severity: this.workstationService.isBusinessError(error, 'SRI_RECEPTION_REJECTED') ? 'warn' : 'error',
          summary: 'Envío SRI',
          detail: this.workstationService.resolveBusinessError(error),
        });
      },
    });
  }

  checkSriAuthorization(saleId: number): void {
    if (!this.canSubmitSriDocuments || this.sriCheckingAuthorizationSaleId() || this.sriProcessingSaleId()) {
      return;
    }

    this.sriCheckingAuthorizationSaleId.set(saleId);

    this.workstationService.checkSriAuthorization(saleId).subscribe({
      next: (sale) => {
        this.sriCheckingAuthorizationSaleId.set(null);
        this.selectedSale.set(sale);
        this.loadSales();
        this.reloadSriAttemptsIfOpen(saleId);
        this.messageService.add({
          severity: sale.sriAuthorizationStatus === 'AUTORIZADO' ? 'success' : 'info',
          summary: sale.sriAuthorizationStatus === 'AUTORIZADO' ? 'Autorizado por SRI' : 'Consulta SRI',
          detail: sale.sriAuthorizationStatus === 'AUTORIZADO'
            ? 'Comprobante autorizado por SRI.'
            : 'Consulta de autorización realizada.',
        });
      },
      error: (error: HttpErrorResponse) => {
        this.sriCheckingAuthorizationSaleId.set(null);
        this.refreshSelectedSale(saleId);
        this.reloadSriAttemptsIfOpen(saleId);
        this.messageService.add({
          severity: this.workstationService.isBusinessError(error, 'SRI_AUTHORIZATION_PENDING') ? 'warn' : 'error',
          summary: 'Autorización SRI',
          detail: this.workstationService.resolveBusinessError(error),
        });
      },
    });
  }

  openSriAttempts(saleId: number): void {
    const sale = this.selectedSale()?.id === saleId ? this.selectedSale() : null;
    this.sriAttemptsSale.set(sale);
    this.sriAttemptsVisible.set(true);
    this.loadSriAttempts(saleId);
  }

  loadSriAttempts(saleId = this.sriAttemptsSale()?.id): void {
    if (!saleId) {
      return;
    }

    this.sriAttemptsLoading.set(true);
    this.sriAttemptsError.set('');

    this.workstationService.getSriSubmissionAttempts(saleId).subscribe({
      next: (attempts) => {
        this.sriAttempts.set(attempts);
        this.sriAttemptsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.sriAttemptsLoading.set(false);
        this.sriAttemptsError.set(this.workstationService.resolveBusinessError(error));
      },
    });
  }

  downloadSriXmlDraft(saleId: number): void {
    this.workstationService.getSriXmlDraft(saleId).subscribe({
      next: (blob) => this.downloadXmlBlob(blob, this.buildXmlFileName(saleId, 'draft')),
      error: (error: HttpErrorResponse) => {
        this.messageService.add({
          severity: 'error',
          summary: 'No se pudo descargar',
          detail: this.workstationService.resolveBusinessError(error),
        });
      },
    });
  }

  downloadSriSignedXml(saleId: number): void {
    this.workstationService.getSriSignedXml(saleId).subscribe({
      next: (blob) => this.downloadXmlBlob(blob, this.buildXmlFileName(saleId, 'signed')),
      error: (error: HttpErrorResponse) => {
        this.messageService.add({
          severity: 'error',
          summary: 'No se pudo descargar',
          detail: this.workstationService.resolveBusinessError(error),
        });
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

  openSaleDetailFromRecent(saleId: number): void {
    this.recentSalesVisible.set(false);
    this.openSaleDetail(saleId);
  }

  openVoidDialog(sale: SaleListItem): void {
    this.saleToVoid.set(sale);
    this.voidVisible.set(true);
  }

  openVoidDialogFromRecent(sale: SaleListItem): void {
    this.recentSalesVisible.set(false);
    this.openVoidDialog(sale);
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

  onQuickSearchVisibleChange(visible: boolean): void {
    this.quickSearchVisible.set(visible);

    if (!visible) {
      this.focusMainSearch();
    }
  }

  handleQuickProductSelection(product: PosProduct): void {
    if (this.addProduct(product)) {
      this.clearSearchAndFocus();
    }
  }

  private hasActiveSriProcessing(): boolean {
    return !!(
      this.sriProcessingSaleId()
      || this.sriSigningSaleId()
      || this.sriSubmittingSaleId()
      || this.sriCheckingAuthorizationSaleId()
    );
  }

  private getSriWorkflowValidationMessage(sale: Sale): string | null {
    if (!sale.id) {
      return 'La venta no es válida para procesamiento SRI.';
    }

    if (sale.documentType !== SaleDocumentType.Invoice) {
      return 'Solo las facturas pueden procesarse con SRI.';
    }

    if (sale.isVoided) {
      return 'No se puede procesar SRI para una venta anulada.';
    }

    if (this.isSriAuthorized(sale)) {
      return 'El comprobante ya está autorizado por SRI.';
    }

    if (!sale.hasSriXmlDraft) {
      return 'La factura no tiene XML draft disponible para procesar SRI.';
    }

    return null;
  }

  private runSriWorkflow(sale: Sale): Observable<Sale> {
    return this.ensureSriXmlSigned(sale).pipe(
      switchMap((signedSale) => this.submitSignedXmlIfNeeded(signedSale)),
      switchMap((submittedSale) => this.workstationService.checkSriAuthorization(submittedSale.id))
    );
  }

  private ensureSriXmlSigned(sale: Sale): Observable<Sale> {
    if (sale.hasSriSignedXml) {
      return of(sale);
    }

    return this.workstationService.signInvoiceXml(sale.id).pipe(
      tap((signedSale) => this.selectedSale.set(signedSale))
    );
  }

  private submitSignedXmlIfNeeded(sale: Sale): Observable<Sale> {
    if (this.isSriReceived(sale)) {
      return of(sale);
    }

    return this.workstationService.submitSriInvoice(sale.id).pipe(
      tap((submittedSale) => {
        this.selectedSale.set(submittedSale);
        this.messageService.add({
          severity: 'success',
          summary: 'Recibido por SRI',
          detail: 'Comprobante recibido por SRI. La autorización aún está pendiente.',
        });
      })
    );
  }

  private handleSriWorkflowSuccess(sale: Sale): void {
    this.selectedSale.set(sale);

    if (this.isSriAuthorized(sale)) {
      this.messageService.add({
        severity: 'success',
        summary: 'Autorizado por SRI',
        detail: 'Comprobante autorizado por SRI.',
      });
      return;
    }

    if (this.isSriAuthorizationPending(sale)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Autorización pendiente',
        detail: 'Comprobante recibido por SRI. La autorización aún está pendiente.',
      });
      return;
    }

    if (this.isSriAuthorizationRejected(sale)) {
      this.messageService.add({
        severity: 'error',
        summary: 'Autorización SRI',
        detail: sale.sriLastSubmissionError || 'El SRI no autorizó el comprobante. Revisa el historial de intentos.',
      });
      return;
    }

    if (this.isSriReceptionRejected(sale)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Recepción SRI',
        detail: sale.sriLastSubmissionError || 'El SRI devolvió el comprobante. Revisa el historial de intentos.',
      });
      return;
    }

    this.messageService.add({
      severity: 'info',
      summary: 'Consulta SRI',
      detail: 'Consulta de autorización realizada.',
    });
  }

  private handleSriWorkflowError(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      this.messageService.add({
        severity: 'error',
        summary: 'Procesamiento SRI',
        detail: 'No se pudo completar el procesamiento SRI.',
      });
      return;
    }

    if (this.workstationService.isBusinessError(error, 'SRI_AUTHORIZATION_PENDING')) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Autorización pendiente',
        detail: 'Comprobante recibido por SRI. La autorización aún está pendiente.',
      });
      return;
    }

    if (this.workstationService.isBusinessError(error, 'SRI_RECEPTION_REJECTED')) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Recepción SRI',
        detail: this.workstationService.resolveBusinessError(error),
      });
      return;
    }

    if (this.workstationService.isBusinessError(error, 'SRI_AUTHORIZATION_REJECTED')) {
      this.messageService.add({
        severity: 'error',
        summary: 'Autorización SRI',
        detail: this.workstationService.resolveBusinessError(error),
      });
      return;
    }

    this.messageService.add({
      severity: 'error',
      summary: 'Procesamiento SRI',
      detail: this.workstationService.resolveBusinessError(error) || 'No se pudo completar el procesamiento SRI.',
    });
  }

  private refreshSriWorkflowContext(saleId: number): void {
    this.refreshSelectedSale(saleId);
    this.loadSales();
    this.reloadSriAttemptsIfOpen(saleId);
  }

  private isSriReceived(sale: Sale): boolean {
    return this.normalizeSriStatus(sale.sriReceptionStatus) === 'RECIBIDA';
  }

  private isSriReceptionRejected(sale: Sale): boolean {
    return this.normalizeSriStatus(sale.sriReceptionStatus) === 'DEVUELTA';
  }

  private isSriAuthorized(sale: Sale): boolean {
    return sale.documentStatus === SaleDocumentStatus.Authorized
      || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO';
  }

  private isSriAuthorizationPending(sale: Sale): boolean {
    return this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'PENDIENTE';
  }

  private isSriAuthorizationRejected(sale: Sale): boolean {
    const authorizationStatus = this.normalizeSriStatus(sale.sriAuthorizationStatus);

    return sale.documentStatus === SaleDocumentStatus.Rejected
      || authorizationStatus === 'NO AUTORIZADO'
      || authorizationStatus === 'NO_AUTORIZADO';
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
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

  private refreshSelectedSale(saleId: number): void {
    if (this.selectedSale()?.id !== saleId) {
      return;
    }

    this.workstationService.getSaleDetail(saleId).subscribe({
      next: (sale) => this.selectedSale.set(sale),
      error: () => undefined,
    });

    this.loadSales();
  }

  private reloadSriAttemptsIfOpen(saleId: number): void {
    if (this.sriAttemptsVisible() && this.sriAttemptsSale()?.id === saleId) {
      this.loadSriAttempts(saleId);
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
            discountAmount: this.normalizeDiscount(item.discountAmount, nextQuantity * item.unitPrice),
            stock: nextStock,
            quantity: nextQuantity,
            product,
          };
        })
        .filter((item): item is CartItem => !!item)
    );

    this.ensureActiveCartLine();
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

  private normalizeDiscount(value: number, maxValue: number): number {
    return roundMoney(Math.min(Math.max(Number(value) || 0, 0), Math.max(maxValue, 0)));
  }

  private handleCartKeyboardShortcut(event: KeyboardEvent): void {
    if (!this.shouldHandleCartKeyboardShortcut(event)) {
      return;
    }

    const key = event.key;

    if (key === 'ArrowDown') {
      this.moveActiveLine(1);
      event.preventDefault();
      return;
    }

    if (key === 'ArrowUp') {
      this.moveActiveLine(-1);
      event.preventDefault();
      return;
    }

    if (key === '+' || key === '=') {
      this.adjustActiveLineQuantity(1);
      event.preventDefault();
      return;
    }

    if (key === '-' || key === '_') {
      this.adjustActiveLineQuantity(-1);
      event.preventDefault();
      return;
    }

    if (key === 'Delete') {
      this.removeActiveLine();
      event.preventDefault();
    }
  }

  private shouldHandleCartKeyboardShortcut(event: KeyboardEvent): boolean {
    if (!this.canSell || !this.cart().length || event.altKey || event.ctrlKey || event.metaKey) {
      return false;
    }

    if (
      this.quickSearchVisible()
      || this.customerSelectorVisible()
      || this.checkoutVisible()
      || this.recentSalesVisible()
      || this.saleDetailVisible()
      || this.voidVisible()
    ) {
      return false;
    }

    const target = event.target as HTMLElement | null;
    if (!this.isEditableTarget(target)) {
      return true;
    }

    return this.isMainSearchInput(target) && this.searchTerm().trim().length === 0;
  }

  private isEditableTarget(target: HTMLElement | null): boolean {
    if (!target) {
      return false;
    }

    const tagName = target.tagName.toLowerCase();
    return tagName === 'input' || tagName === 'textarea' || target.isContentEditable;
  }

  private isMainSearchInput(target: HTMLElement | null): boolean {
    return target?.getAttribute('aria-label') === 'Escanear o buscar producto';
  }

  private moveActiveLine(direction: 1 | -1): void {
    const items = this.cart();
    if (!items.length) {
      this.activeCartProductId.set(null);
      return;
    }

    const currentIndex = Math.max(
      items.findIndex((item) => item.productId === this.activeCartProductId()),
      0
    );
    const nextIndex = Math.min(Math.max(currentIndex + direction, 0), items.length - 1);
    this.activeCartProductId.set(items[nextIndex].productId);
  }

  private adjustActiveLineQuantity(delta: 1 | -1): void {
    const activeItem = this.getActiveCartItem();
    if (!activeItem) {
      return;
    }

    const nextQuantity = Math.max(1, activeItem.quantity + delta);
    this.updateQuantity({ productId: activeItem.productId, quantity: nextQuantity });
  }

  private removeActiveLine(): void {
    const activeItem = this.getActiveCartItem();
    if (!activeItem) {
      return;
    }

    this.removeItem(activeItem.productId);
  }

  private getActiveCartItem(): CartItem | null {
    this.ensureActiveCartLine();
    return this.cart().find((item) => item.productId === this.activeCartProductId()) ?? null;
  }

  private moveActiveLineAfterRemoval(removedIndex: number): void {
    const items = this.cart();

    if (!items.length) {
      this.activeCartProductId.set(null);
      return;
    }

    const nextIndex = Math.min(Math.max(removedIndex, 0), items.length - 1);
    this.activeCartProductId.set(items[nextIndex].productId);
  }

  private ensureActiveCartLine(): void {
    const items = this.cart();

    if (!items.length) {
      this.activeCartProductId.set(null);
      return;
    }

    const activeProductId = this.activeCartProductId();
    if (!activeProductId || !items.some((item) => item.productId === activeProductId)) {
      this.activeCartProductId.set(items[items.length - 1].productId);
    }
  }

  private findExactIdentifierMatch(value: string): PosProduct | null {
    const term = value.trim().toLowerCase();
    if (!term.length) {
      return null;
    }

    return this.allProducts().find((product) =>
      this.sameIdentifier(product.barcode, term) || this.sameIdentifier(product.internalCode, term)
    ) ?? null;
  }

  private productMatchesTerm(product: PosProduct, term: string): boolean {
    return this.productSearchText(product).some((value) => value.includes(term));
  }

  private productMatchRank(product: PosProduct, term: string): number {
    if (this.sameIdentifier(product.barcode, term) || this.sameIdentifier(product.internalCode, term)) {
      return 0;
    }

    if (product.name.toLowerCase().startsWith(term)) {
      return 1;
    }

    return 2;
  }

  private sameIdentifier(value: string | null | undefined, term: string): boolean {
    return !!value && value.trim().toLowerCase() === term;
  }

  private productSearchText(product: PosProduct): string[] {
    return [product.name, product.barcode ?? '', product.internalCode ?? '']
      .map((value) => value.trim().toLowerCase())
      .filter((value) => value.length > 0);
  }

  private clearSearchAndFocus(): void {
    this.searchTerm.set('');
    this.focusMainSearch();
  }

  private focusMainSearch(): void {
    this.productSearchPanel?.focusSearchInput();
  }

  private downloadXmlBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  private buildXmlFileName(saleId: number, suffix: 'draft' | 'signed'): string {
    const sale = this.selectedSale()?.id === saleId ? this.selectedSale() : null;
    const fallback = this.sales().find((item) => item.id === saleId);
    const identifier = sale?.number ?? fallback?.number ?? String(saleId);
    return `factura-${this.sanitizeFileNamePart(identifier)}-${suffix}.xml`;
  }

  private sanitizeFileNamePart(value: string): string {
    return value.trim().replace(/[^a-zA-Z0-9-]/g, '-') || 'sin-numero';
  }

  private closeContextualDialog(): void {
    if (this.quickSearchVisible()) {
      this.quickSearchVisible.set(false);
      this.focusMainSearch();
      return;
    }

    if (this.customerSelectorVisible()) {
      this.customerSelectorVisible.set(false);
      this.focusMainSearch();
      return;
    }

    if (this.checkoutVisible()) {
      this.checkoutVisible.set(false);
      this.focusMainSearch();
      return;
    }

    if (this.recentSalesVisible()) {
      this.recentSalesVisible.set(false);
      this.focusMainSearch();
      return;
    }

    if (this.saleDetailVisible()) {
      if (this.sriAttemptsVisible()) {
        this.sriAttemptsVisible.set(false);
        return;
      }

      this.saleDetailVisible.set(false);
      this.focusMainSearch();
      return;
    }

    if (this.voidVisible()) {
      this.voidVisible.set(false);
      this.focusMainSearch();
    }
  }
}
