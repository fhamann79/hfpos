import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { ToolbarModule } from 'primeng/toolbar';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { Product } from '../../../catalog/models/product.model';
import { ProductService } from '../../../catalog/services/product.service';
import { Supplier } from '../../../suppliers/models/supplier.model';
import { SupplierService } from '../../../suppliers/services/supplier.service';
import {
  CreatePurchaseReceiptRequest,
  PurchaseReceipt,
  PurchaseReceiptListItem,
  PurchaseReceiptStatus,
} from '../../models/purchase-receipt.model';
import { PurchaseReceiptService } from '../../services/purchase-receipt.service';

interface SelectOption<T> {
  label: string;
  value: T;
}

interface ReceiptDraftItem {
  uid: number;
  productId: number | null;
  quantity: number | null;
  unitCost: number | null;
  notes: string;
}

@Component({
  selector: 'app-purchase-receipts-page',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
    DatePipe,
    FormsModule,
    TableModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TagModule,
    TextareaModule,
    ToastModule,
    ToolbarModule,
  ],
  providers: [MessageService],
  templateUrl: './purchase-receipts-page.html',
  styleUrl: './purchase-receipts-page.scss',
})
export class PurchaseReceiptsPage implements OnInit {
  private readonly purchaseReceiptService = inject(PurchaseReceiptService);
  private readonly supplierService = inject(SupplierService);
  private readonly productService = inject(ProductService);
  private readonly permissionService = inject(PermissionService);
  private readonly messageService = inject(MessageService);

  readonly receipts = signal<PurchaseReceiptListItem[]>([]);
  readonly suppliers = signal<Supplier[]>([]);
  readonly products = signal<Product[]>([]);
  readonly selectedReceipt = signal<PurchaseReceipt | null>(null);
  readonly draftItems = signal<ReceiptDraftItem[]>([]);
  readonly loading = signal(false);
  readonly catalogLoading = signal(false);
  readonly saving = signal(false);
  readonly detailLoading = signal(false);
  readonly canceling = signal(false);
  readonly errorMessage = signal('');
  readonly formError = signal('');
  readonly detailError = signal('');
  readonly cancelError = signal('');

  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.purchasesWrite));
  readonly activeSuppliers = computed(() => this.suppliers().filter((supplier) => supplier.isActive));
  readonly activeProducts = computed(() => this.products().filter((product) => product.isActive));
  readonly totalReceived = computed(() =>
    this.receipts()
      .filter((receipt) => receipt.status === PurchaseReceiptStatus.Posted)
      .reduce((sum, receipt) => sum + receipt.subtotal, 0)
  );
  readonly postedCount = computed(() => this.receipts().filter((receipt) => receipt.status === PurchaseReceiptStatus.Posted).length);
  readonly canceledCount = computed(() => this.receipts().filter((receipt) => receipt.status === PurchaseReceiptStatus.Canceled).length);
  readonly subtotal = computed(() => this.draftItems().reduce((sum, item) => sum + this.lineTotal(item), 0));

  readonly supplierOptions = computed<SelectOption<number>[]>(() =>
    this.activeSuppliers().map((supplier) => ({
      label: supplier.identification ? `${supplier.name} - ${supplier.identification}` : supplier.name,
      value: supplier.id,
    }))
  );

  readonly productOptions = computed<SelectOption<number>[]>(() =>
    this.activeProducts().map((product) => ({
      label: `${product.name} - costo ${this.formatCompactMoney(product.cost)}`,
      value: product.id,
    }))
  );

  readonly statusOptions: SelectOption<PurchaseReceiptStatus>[] = [
    { label: 'Publicadas', value: PurchaseReceiptStatus.Posted },
    { label: 'Canceladas', value: PurchaseReceiptStatus.Canceled },
  ];

  search = '';
  from = '';
  to = '';
  status: PurchaseReceiptStatus | null = null;
  createDialogVisible = false;
  detailDialogVisible = false;
  cancelDialogVisible = false;
  supplierId: number | null = null;
  receiptNumber = '';
  supplierDocumentNumber = '';
  receiptDate = this.formatDateInput(new Date());
  notes = '';
  cancelReason = '';

  ngOnInit(): void {
    this.loadReferenceData();
    this.loadReceipts();
  }

  loadReferenceData(): void {
    this.catalogLoading.set(true);

    this.supplierService.getAll().subscribe({
      next: (suppliers) => {
        this.suppliers.set(suppliers);
        this.catalogLoading.set(false);
      },
      error: () => {
        this.catalogLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'No se pudieron cargar los proveedores.',
        });
      },
    });

    this.productService.getAll().subscribe({
      next: (products) => this.products.set(products),
      error: () => {
        this.messageService.add({
          severity: 'error',
          summary: 'Error',
          detail: 'No se pudieron cargar los productos.',
        });
      },
    });
  }

  loadReceipts(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.purchaseReceiptService
      .getAll({
        search: this.search,
        from: this.from,
        to: this.to,
        status: this.status,
      })
      .subscribe({
        next: (receipts) => {
          this.receipts.set(receipts);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          this.errorMessage.set(resolveHttpErrorMessage(error, 'No se pudieron cargar las recepciones.'));
        },
      });
  }

  clearFilters(): void {
    this.search = '';
    this.from = '';
    this.to = '';
    this.status = null;
    this.loadReceipts();
  }

  openCreateDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.resetForm();
    this.createDialogVisible = true;
  }

  closeCreateDialog(): void {
    this.createDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  addItem(): void {
    this.draftItems.update((items) => [
      ...items,
      {
        uid: Date.now() + items.length,
        productId: null,
        quantity: 1,
        unitCost: 0,
        notes: '',
      },
    ]);
  }

  removeItem(uid: number): void {
    this.draftItems.update((items) => items.filter((item) => item.uid !== uid));
  }

  updateItemProduct(uid: number, productId: number | null): void {
    const product = productId === null ? null : this.products().find((item) => item.id === productId);

    this.draftItems.update((items) =>
      items.map((item) =>
        item.uid === uid
          ? {
              ...item,
              productId,
              unitCost: product ? product.cost : item.unitCost,
            }
          : item
      )
    );
  }

  updateItemNumber(uid: number, field: 'quantity' | 'unitCost', value: string | number | null): void {
    const parsed = this.parseNullableNumber(value);
    this.draftItems.update((items) => items.map((item) => (item.uid === uid ? { ...item, [field]: parsed } : item)));
  }

  updateItemNotes(uid: number, notes: string): void {
    this.draftItems.update((items) => items.map((item) => (item.uid === uid ? { ...item, notes } : item)));
  }

  saveReceipt(): void {
    if (!this.canWrite()) {
      return;
    }

    const validationError = this.validateForm();
    this.formError.set(validationError);

    if (validationError) {
      return;
    }

    this.saving.set(true);

    this.purchaseReceiptService.create(this.buildPayload()).subscribe({
      next: (receipt) => {
        this.saving.set(false);
        this.messageService.add({
          severity: 'success',
          summary: 'Recepcion registrada',
          detail: 'El inventario fue actualizado correctamente.',
        });
        this.closeCreateDialog();
        this.selectedReceipt.set(receipt);
        this.detailDialogVisible = true;
        this.loadReceipts();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.formError.set(resolveHttpErrorMessage(error, 'No se pudo registrar la recepcion.'));
      },
    });
  }

  openDetail(receipt: PurchaseReceiptListItem): void {
    this.detailDialogVisible = true;
    this.detailLoading.set(true);
    this.detailError.set('');
    this.selectedReceipt.set(null);

    this.purchaseReceiptService.getById(receipt.id).subscribe({
      next: (detail) => {
        this.selectedReceipt.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(resolveHttpErrorMessage(error, 'No se pudo cargar el detalle de la recepcion.'));
      },
    });
  }

  closeDetailDialog(): void {
    this.detailDialogVisible = false;
    this.selectedReceipt.set(null);
    this.detailError.set('');
  }

  openCancelDialog(): void {
    const receipt = this.selectedReceipt();
    if (!this.canWrite() || !receipt || receipt.status !== PurchaseReceiptStatus.Posted) {
      return;
    }

    this.cancelReason = '';
    this.cancelError.set('');
    this.cancelDialogVisible = true;
  }

  closeCancelDialog(): void {
    this.cancelDialogVisible = false;
    this.cancelReason = '';
    this.cancelError.set('');
    this.canceling.set(false);
  }

  confirmCancelReceipt(): void {
    const receipt = this.selectedReceipt();
    if (!this.canWrite() || !receipt || receipt.status !== PurchaseReceiptStatus.Posted) {
      return;
    }

    const reason = this.cancelReason.trim();
    if (!reason || reason.length > 500) {
      this.cancelError.set('Ingresa una razón de cancelación válida.');
      return;
    }

    this.canceling.set(true);
    this.cancelError.set('');

    this.purchaseReceiptService.cancel(receipt.id, { reason }).subscribe({
      next: (updatedReceipt) => {
        this.canceling.set(false);
        this.selectedReceipt.set(updatedReceipt);
        this.closeCancelDialog();
        this.messageService.add({
          severity: 'success',
          summary: 'Recepción cancelada',
          detail: 'El stock ingresado fue revertido con movimientos auditables.',
        });
        this.loadReceipts();
      },
      error: (error: HttpErrorResponse) => {
        this.canceling.set(false);
        this.cancelError.set(resolveHttpErrorMessage(error, 'No se pudo cancelar la recepción.'));
      },
    });
  }

  lineTotal(item: ReceiptDraftItem): number {
    const quantity = item.quantity ?? 0;
    const unitCost = item.unitCost ?? 0;
    return quantity * unitCost;
  }

  statusLabel(status: PurchaseReceiptStatus): string {
    if (status === PurchaseReceiptStatus.Posted) {
      return 'Publicada';
    }

    if (status === PurchaseReceiptStatus.Canceled) {
      return 'Cancelada';
    }

    return String(status);
  }

  statusSeverity(status: PurchaseReceiptStatus): 'success' | 'danger' | 'secondary' {
    if (status === PurchaseReceiptStatus.Posted) {
      return 'success';
    }

    if (status === PurchaseReceiptStatus.Canceled) {
      return 'danger';
    }

    return 'secondary';
  }

  isPosted(receipt: PurchaseReceipt | PurchaseReceiptListItem): boolean {
    return receipt.status === PurchaseReceiptStatus.Posted;
  }

  isCanceled(receipt: PurchaseReceipt | PurchaseReceiptListItem): boolean {
    return receipt.status === PurchaseReceiptStatus.Canceled;
  }

  rowClass(receipt: PurchaseReceiptListItem): string {
    return receipt.status === PurchaseReceiptStatus.Canceled ? 'row-canceled' : '';
  }

  productCost(productId: number | null): number | null {
    if (productId === null) {
      return null;
    }

    return this.products().find((product) => product.id === productId)?.cost ?? null;
  }

  private resetForm(): void {
    this.supplierId = null;
    this.receiptNumber = '';
    this.supplierDocumentNumber = '';
    this.receiptDate = this.formatDateInput(new Date());
    this.notes = '';
    this.formError.set('');
    this.draftItems.set([]);
    this.addItem();
  }

  private validateForm(): string {
    if (!this.supplierId) {
      return 'Selecciona un proveedor.';
    }

    if (this.draftItems().length === 0) {
      return 'Agrega al menos un producto.';
    }

    for (const item of this.draftItems()) {
      if (!item.productId) {
        return 'Selecciona producto en todas las lineas.';
      }

      if (item.quantity === null || item.quantity <= 0) {
        return 'La cantidad debe ser mayor a 0.';
      }

      if (item.unitCost === null || item.unitCost < 0) {
        return 'El costo unitario debe ser mayor o igual a 0.';
      }
    }

    return '';
  }

  private buildPayload(): CreatePurchaseReceiptRequest {
    return {
      supplierId: this.supplierId ?? 0,
      receiptNumber: this.normalizeOptionalText(this.receiptNumber),
      supplierDocumentNumber: this.normalizeOptionalText(this.supplierDocumentNumber),
      receiptDate: `${this.receiptDate}T00:00:00Z`,
      notes: this.normalizeOptionalText(this.notes),
      items: this.draftItems().map((item) => ({
        productId: item.productId ?? 0,
        quantity: item.quantity ?? 0,
        unitCost: item.unitCost ?? 0,
        notes: this.normalizeOptionalText(item.notes),
      })),
    };
  }

  private normalizeOptionalText(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private parseNullableNumber(value: string | number | null): number | null {
    if (value === null || value === '') {
      return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private formatDateInput(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private formatCompactMoney(value: number): string {
    return `$${value.toFixed(4)}`;
  }
}
