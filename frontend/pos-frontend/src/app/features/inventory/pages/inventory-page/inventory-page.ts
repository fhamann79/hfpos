import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TabsModule } from 'primeng/tabs';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { ToastModule } from 'primeng/toast';
import { ToolbarModule } from 'primeng/toolbar';
import { forkJoin } from 'rxjs';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  formatBusinessDateTime as formatBusinessDateTimeValue,
  formatBusinessTime as formatBusinessTimeValue,
} from '../../../../core/utils/business-date-format';
import { readErrorCode } from '../../../../core/utils/http-error-normalizer';
import { OperationIntent, definitiveOperationRejection, operationActor, operationScope } from '../../../../core/utils/operation-intent';
import {
  InventoryMovement,
  InventoryMovementSourceType,
  InventoryMovementType,
} from '../../models/inventory-movement.model';
import { InventoryMovementFilters } from '../../models/inventory-filters.model';
import { InventoryOperationRequest } from '../../models/inventory-operation.model';
import { InventoryStock, InventoryStockSummary, StockStatus } from '../../models/inventory-stock.model';
import { InventoryService } from '../../services/inventory.service';
import { InventoryTransferPanel } from '../../components/inventory-transfer-panel/inventory-transfer-panel';

interface SelectOption<T> {
  label: string;
  value: T;
}

interface ContextItem {
  label: string;
  value: number | null;
}

type InventoryOperationKind = 'entry' | 'exit' | 'adjust';

interface InventoryOperationForm {
  productId: number | null;
  quantity: number | null;
  reference: string;
  notes: string;
}

@Component({
  selector: 'app-inventory-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TableModule,
    ButtonModule,
    CardModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    MessageModule,
    TabsModule,
    TagModule,
    TextareaModule,
    ToggleSwitchModule,
    ToastModule,
    ToolbarModule,
    InventoryTransferPanel,
  ],
  providers: [MessageService],
  templateUrl: './inventory-page.html',
  styleUrls: ['./inventory-page.scss', './inventory-operations.scss'],
})
export class InventoryPage implements OnInit, OnDestroy {
  @ViewChild('kardexSection') private kardexSection?: ElementRef<HTMLElement>;

  private readonly inventoryService = inject(InventoryService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);
  private readonly messageService = inject(MessageService);
  readonly canReadInventory = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryRead));
  readonly canWriteInventory = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryWrite));
  readonly contextItems = computed<ContextItem[]>(() => [
    { label: 'CompanyId', value: this.authStore.companyId() },
    { label: 'EstablishmentId', value: this.authStore.establishmentId() },
    { label: 'EmissionPointId', value: this.authStore.emissionPointId() },
  ]);

  readonly stocks = signal<InventoryStock[]>([]);
  readonly stockLoading = signal(false);
  readonly stockError = signal('');
  readonly stockSummary = signal<InventoryStockSummary>({ totalProducts: 0, outOfStockProducts: 0,
    lowStockProducts: 0, inactiveProducts: 0, totalInventoryUnits: 0, totalInventoryValue: 0 });
  readonly totalProducts = computed(() => this.stockSummary().totalProducts);
  readonly outOfStockProducts = computed(() => this.stockSummary().outOfStockProducts);
  readonly lowStockProducts = computed(() => this.stockSummary().lowStockProducts);
  readonly inactiveProducts = computed(() => this.stockSummary().inactiveProducts);
  readonly totalInventoryUnits = computed(() => this.stockSummary().totalInventoryUnits);
  readonly totalInventoryValue = computed(() => this.stockSummary().totalInventoryValue);
  readonly totalStockItems = signal(0);
  readonly lookupStocks = signal<InventoryStock[]>([]);
  readonly lookupLoading = signal(false);
  readonly lookupError = signal('');
  readonly stockRevision = signal(0);
  stockFirst = 0;
  stockRows = 30;
  private stockSequence = 0;
  private lookupSequence = 0;
  private lookupTimer?: ReturnType<typeof setTimeout>;
  readonly companyTimeZoneId = computed(() => this.authStore.companyTimeZoneId());

  readonly movements = signal<InventoryMovement[]>([]);
  readonly movementsLoading = signal(false);
  readonly movementsError = signal('');
  readonly totalMovementItems = signal(0);
  readonly totalMovementPages = signal(0);
  readonly lastMovement = computed(() => this.movements()[0] ?? null);
  focusedProduct(): InventoryStock | null {
    const productId = this.movementProductId;
    return productId === null ? null : (this.lookupStocks().find((stock) => stock.productId === productId) ?? null);
  }

  readonly selectedMovement = signal<InventoryMovement | null>(null);
  readonly movementDetailLoading = signal(false);
  readonly movementDetailError = signal('');

  readonly productOptions = computed<SelectOption<number>[]>(() =>
    this.lookupStocks().map((stock) => ({
      label: `${stock.productId} - ${stock.productName}${stock.isActive ? '' : ' (Inactivo)'}`,
      value: stock.productId,
    }))
  );

  readonly movementTypeOptions: SelectOption<InventoryMovementType>[] = [
    { label: 'Inicial', value: InventoryMovementType.Initial },
    { label: 'Entrada', value: InventoryMovementType.Entry },
    { label: 'Salida', value: InventoryMovementType.Exit },
    { label: 'Ajuste', value: InventoryMovementType.Adjustment },
    { label: 'Venta', value: InventoryMovementType.Sale },
    { label: 'Anulación', value: InventoryMovementType.Void },
    { label: 'Devolución', value: InventoryMovementType.Return },
  ];

  readonly sourceTypeOptions: SelectOption<InventoryMovementSourceType>[] = [
    { label: 'Entrada manual', value: InventoryMovementSourceType.ManualEntry },
    { label: 'Salida manual', value: InventoryMovementSourceType.ManualExit },
    { label: 'Ajuste manual', value: InventoryMovementSourceType.ManualAdjustment },
    { label: 'Venta', value: InventoryMovementSourceType.Sale },
    { label: 'Anulación de venta', value: InventoryMovementSourceType.SaleVoid },
    { label: 'Recepción de compra', value: InventoryMovementSourceType.PurchaseReceipt },
    { label: 'Cancelación recepción de compra', value: InventoryMovementSourceType.PurchaseReceiptCancel },
    { label: 'Nota de crédito', value: InventoryMovementSourceType.CreditNoteReturn },
    { label: 'Transferencia enviada', value: InventoryMovementSourceType.InventoryTransferOut },
    { label: 'Transferencia recibida', value: InventoryMovementSourceType.InventoryTransferIn },
    { label: 'Inventario de apertura', value: InventoryMovementSourceType.OpeningInventory },
  ];

  readonly operationOptions: SelectOption<InventoryOperationKind>[] = [
    { label: 'Entrada', value: 'entry' },
    { label: 'Salida', value: 'exit' },
    { label: 'Ajuste', value: 'adjust' },
  ];

  stockSearch = '';
  stockProductId: number | null = null;
  stockOnlyPositive = false;

  movementProductId: number | null = null;
  movementType: InventoryMovementType | null = null;
  movementSourceType: InventoryMovementSourceType | null = null;
  movementSourceId: number | null = null;
  movementUserId: number | null = null;
  movementSearch = '';
  movementFrom = '';
  movementTo = '';
  movementFirst = 0;
  movementRows = 25;
  movementDetailVisible = false;
  activeOperation: InventoryOperationKind = 'entry';
  operationLoading = signal(false);
  operationError = signal('');
  operationResult = signal<InventoryMovement | null>(null);
  readonly countSnapshot = signal<import('../../models/inventory-operation.model').InventoryCountSnapshot | null>(null);
  readonly countLoading = signal(false);
  private countSequence = 0;
  private movementSequence = 0;
  private detailSequence = 0;
  private destroyed = false;
  private readonly operationIntent = new OperationIntent<{ kind: InventoryOperationKind; payload: InventoryOperationRequest }>(
    'inventory', operationActor(this.authStore));
  readonly operationLocked = signal(this.operationIntent.pending);
  entryForm: InventoryOperationForm = this.createOperationForm();
  exitForm: InventoryOperationForm = this.createOperationForm();
  adjustForm: InventoryOperationForm = this.createOperationForm();

  ngOnInit(): void {
    if (this.operationIntent.pending) {
      try {
        const pending = this.operationIntent.retry(operationScope(this.authStore));
        this.activeOperation = pending.kind;
        Object.assign(this.currentOperationForm(), pending.payload);
        this.operationError.set('Resultado pendiente. Reintenta la misma operacion para recuperarlo.');
      } catch (error) {
        this.operationError.set(error instanceof Error ? error.message : 'Operacion pendiente.');
      }
    }
    if (this.canReadInventory()) {
      this.refreshAll();
    }
  }

  refreshAll(): void {
    this.loadStocks();
    this.loadProductOptions('', ++this.lookupSequence, true);
    this.stockRevision.update(revision => revision + 1);
    this.loadMovements(1, this.movementRows);
  }

  onTransferComplete(): void {
    this.refreshAll();
  }

  loadStocks(page = Math.floor(this.stockFirst / this.stockRows) + 1, pageSize = this.stockRows): void {
    const sequence = ++this.stockSequence;
    this.stockLoading.set(true);
    this.stockError.set('');

    this.inventoryService
      .getStocks(this.cleanText(this.stockSearch), this.stockProductId, this.stockOnlyPositive, page, pageSize)
      .subscribe({
        next: (result) => {
          if (sequence !== this.stockSequence) return;
          this.stocks.set(result.items);
          this.stockSummary.set(result.summary);
          this.totalStockItems.set(result.totalItems);
          this.stockRows = result.pageSize;
          this.stockFirst = (result.page - 1) * result.pageSize;
          this.stockLoading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          if (sequence !== this.stockSequence) return;
          this.stockLoading.set(false);
          this.stockError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el stock actual.'));
        },
      });
  }

  applyStockFilters(): void {
    this.stockFirst = 0;
    this.loadStocks(1, this.stockRows);
  }

  clearStockFilters(): void {
    this.stockSearch = '';
    this.stockProductId = null;
    this.stockOnlyPositive = false;
    this.applyStockFilters();
  }

  onStocksLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.stockRows;
    this.loadStocks(Math.floor((event.first ?? 0) / rows) + 1, rows);
  }

  searchProductOptions(search: string): void {
    clearTimeout(this.lookupTimer);
    const sequence = ++this.lookupSequence;
    this.lookupLoading.set(true);
    this.lookupTimer = setTimeout(() => this.loadProductOptions(search, sequence), 300);
  }

  loadProductOptions(search = '', sequence = ++this.lookupSequence, refreshSelected = false): void {
    if (sequence !== this.lookupSequence) return;
    this.lookupLoading.set(true);
    this.lookupError.set('');
    const selected = new Set([this.stockProductId, this.movementProductId,
      this.entryForm.productId, this.exitForm.productId, this.adjustForm.productId]);
    const requests = [this.inventoryService.getStocks(search.trim() || null, null, false, 1, 30)];
    if (refreshSelected) {
      for (const id of selected) {
        if (id !== null) requests.push(this.inventoryService.getStocks(null, id, false, 1, 1));
      }
    }
    forkJoin(requests).subscribe({
      next: results => {
        if (sequence !== this.lookupSequence) return;
        const products = new Map(results.flatMap(result => result.items).map(item => [item.productId, item]));
        const currentSelected = new Set([this.stockProductId, this.movementProductId,
          this.entryForm.productId, this.exitForm.productId, this.adjustForm.productId]);
        for (const stock of this.lookupStocks()) {
          if (currentSelected.has(stock.productId) && !products.has(stock.productId)
            && (!refreshSelected || !selected.has(stock.productId))) products.set(stock.productId, stock);
        }
        this.lookupStocks.set([...products.values()]);
        this.lookupLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (sequence !== this.lookupSequence) return;
        this.lookupLoading.set(false);
        this.lookupError.set(this.inventoryService.resolveError(error, 'No se pudieron buscar los productos.'));
      },
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    ++this.movementSequence;
    ++this.detailSequence;
    ++this.countSequence;
    clearTimeout(this.lookupTimer);
    ++this.lookupSequence;
    ++this.stockSequence;
  }

  loadMovements(page: number, pageSize: number): void {
    const sequence = ++this.movementSequence;
    const scope = operationScope(this.authStore);
    this.movementsLoading.set(true);
    this.movementsError.set('');

    this.inventoryService.getMovements(this.buildMovementFilters(page, pageSize)).subscribe({
      next: (result) => {
        if (this.destroyed || sequence !== this.movementSequence || scope !== operationScope(this.authStore)) return;
        this.movements.set(result.items);
        this.totalMovementItems.set(result.totalItems);
        this.totalMovementPages.set(result.totalPages);
        this.movementRows = result.pageSize;
        this.movementFirst = (result.page - 1) * result.pageSize;
        this.movementsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || sequence !== this.movementSequence || scope !== operationScope(this.authStore)) return;
        this.movementsLoading.set(false);
        this.movementsError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el kardex.'));
      },
    });
  }

  onMovementsLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.movementRows;
    const first = event.first ?? 0;

    this.loadMovements(Math.floor(first / rows) + 1, rows);
  }

  applyMovementFilters(): void {
    this.movementFirst = 0;
    this.loadMovements(1, this.movementRows);
  }

  clearMovementFilters(): void {
    this.movementProductId = null;
    this.movementType = null;
    this.movementSourceType = null;
    this.movementSourceId = null;
    this.movementUserId = null;
    this.movementSearch = '';
    this.movementFrom = '';
    this.movementTo = '';
    this.applyMovementFilters();
  }

  focusProductMovements(stock: InventoryStock): void {
    if (!this.lookupStocks().some(item => item.productId === stock.productId)) {
      this.lookupStocks.update(items => [...items, stock]);
    }
    this.movementProductId = stock.productId;
    this.movementSearch = '';
    this.movementFirst = 0;
    this.loadMovements(1, this.movementRows);
    queueMicrotask(() => this.kardexSection?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  clearProductFocus(): void {
    this.movementProductId = null;
    this.applyMovementFilters();
  }

  submitOperation(): void {
    if (this.operationLoading() || this.destroyed || !this.canWriteInventory()) return;
    const wasRetry = this.operationIntent.pending;
    const scope = operationScope(this.authStore);
    const form = this.currentOperationForm();
    const validationError = wasRetry ? '' : this.validateOperationForm(this.activeOperation, form);

    this.operationError.set('');
    this.operationResult.set(null);

    if (validationError) {
      this.operationError.set(validationError);
      return;
    }

    let payload = this.buildOperationPayload(form);
    let kind = this.activeOperation;
    if (kind === 'adjust' && !wasRetry) {
      const snapshot = this.countSnapshot();
      if (!snapshot || snapshot.productId !== payload.productId || this.countLoading()) {
        this.operationError.set('Carga el saldo del conteo antes de confirmar.');
        return;
      }
      payload.expectedMovementWatermark = snapshot.movementWatermark;
      payload.expectedQuantity = snapshot.quantity;
      payload.expectedCompanyId = snapshot.companyId;
      payload.expectedEstablishmentId = snapshot.establishmentId;
    }
    try {
      const frozen = this.operationIntent.capture(scope, { kind, payload });
      payload = frozen.payload;
      kind = frozen.kind;
    } catch (error) {
      this.operationError.set(error instanceof Error ? error.message : 'No se pudo conservar la solicitud.');
      return;
    }
    this.operationLocked.set(true);
    this.operationLoading.set(true);

    const request =
      kind === 'entry'
        ? this.inventoryService.registerEntry(payload)
        : kind === 'exit'
          ? this.inventoryService.registerExit(payload)
          : this.inventoryService.registerAdjustment(payload);

    request.subscribe({
      next: (movement) => {
        if (this.destroyed || scope !== operationScope(this.authStore)) return;
        this.operationIntent.clear();
        this.operationLocked.set(false);
        this.operationLoading.set(false);
        this.operationResult.set(movement);
        this.messageService.add({
          severity: 'success',
          summary: 'Inventario actualizado',
          detail: `${this.operationLabel(kind)} registrada correctamente.`,
        });
        this.resetCurrentOperationForm();
        this.movementProductId = movement.productId;
        this.movementType = movement.type;
        this.loadStocks();
        this.loadProductOptions('', ++this.lookupSequence, true);
        this.stockRevision.update(revision => revision + 1);
        this.applyMovementFilters();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || scope !== operationScope(this.authStore)) return;
        this.operationLoading.set(false);
        if (!wasRetry && definitiveOperationRejection(error)) {
          this.operationIntent.clear();
          this.operationLocked.set(false);
        }
        this.operationError.set(this.resolveOperationError(error));
        if (kind === 'adjust' && !this.operationIntent.pending) this.countSnapshot.set(null);
      },
    });
  }

  openMovementDetail(movement: InventoryMovement): void {
    const sequence = ++this.detailSequence;
    const scope = operationScope(this.authStore);
    this.movementDetailVisible = true;
    this.selectedMovement.set(null);
    this.movementDetailError.set('');
    this.movementDetailLoading.set(true);

    this.inventoryService.getMovementById(movement.id).subscribe({
      next: (detail) => {
        if (this.destroyed || sequence !== this.detailSequence || !this.movementDetailVisible
          || scope !== operationScope(this.authStore) || detail.id !== movement.id) return;
        this.selectedMovement.set(detail);
        this.movementDetailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || sequence !== this.detailSequence || !this.movementDetailVisible
          || scope !== operationScope(this.authStore)) return;
        this.movementDetailLoading.set(false);
        this.movementDetailError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el movimiento.'));
      },
    });
  }

  onMovementDetailVisibleChange(visible: boolean): void {
    this.movementDetailVisible = visible;
    if (!visible) {
      ++this.detailSequence;
      this.movementDetailLoading.set(false);
      this.selectedMovement.set(null);
      this.movementDetailError.set('');
    }
  }

  parseNullableNumber(value: string | number | null): number | null {
    if (value === null || value === '') {
      return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  movementTypeName(type: InventoryMovementType): string {
    return this.movementTypeOptions.find((option) => option.value === type)?.label ?? String(type);
  }

  sourceTypeName(sourceType: InventoryMovementSourceType): string {
    return this.sourceTypeOptions.find((option) => option.value === sourceType)?.label ?? String(sourceType);
  }

  statusSeverity(isActive: boolean): 'success' | 'danger' {
    return isActive ? 'success' : 'danger';
  }

  stockStatusLabel(stock: InventoryStock): string {
    return this.resolveStockStatus(stock) === StockStatus.OutOfStock ? 'Sin stock' : 'Disponible';
  }

  stockStatusSeverity(stock: InventoryStock): 'success' | 'danger' {
    return this.resolveStockStatus(stock) === StockStatus.OutOfStock ? 'danger' : 'success';
  }

  stockRiskLabel(stock: InventoryStock): string {
    const status = this.resolveStockStatus(stock);

    if (status === StockStatus.OutOfStock) {
      return 'Crítico';
    }

    if (status === StockStatus.LowStock) {
      return 'Bajo';
    }

    return 'Normal';
  }

  stockRiskClass(stock: InventoryStock): string {
    const classes = ['stock-risk'];
    const status = this.resolveStockStatus(stock);

    if (status === StockStatus.OutOfStock) {
      classes.push('stock-risk--critical');
    } else if (status === StockStatus.LowStock) {
      classes.push('stock-risk--warning');
    }

    if (!stock.isActive) {
      classes.push('stock-risk--inactive');
    }

    return classes.join(' ');
  }

  stockRowClass(stock: InventoryStock): string {
    const classes: string[] = [];
    const status = this.resolveStockStatus(stock);

    if (status === StockStatus.OutOfStock) {
      classes.push('row-critical');
    } else if (status === StockStatus.LowStock) {
      classes.push('row-warning');
    }

    if (!stock.isActive) {
      classes.push('row-inactive');
    }

    return classes.join(' ');
  }

  stockRiskIcon(stock: InventoryStock): string {
    const status = this.resolveStockStatus(stock);

    if (status === StockStatus.OutOfStock) {
      return 'pi pi-times-circle';
    }

    if (status === StockStatus.LowStock) {
      return 'pi pi-exclamation-triangle';
    }

    return 'pi pi-check-circle';
  }

  movementSeverity(type: InventoryMovementType): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined {
    if (type === InventoryMovementType.Entry || type === InventoryMovementType.Return) {
      return 'success';
    }

    if (type === InventoryMovementType.Sale) {
      return 'info';
    }

    if (type === InventoryMovementType.Exit || type === InventoryMovementType.Void) {
      return 'danger';
    }

    if (type === InventoryMovementType.Adjustment) {
      return 'contrast';
    }

    return 'secondary';
  }

  sourceBadgeClass(sourceType: InventoryMovementSourceType): string {
    if (sourceType === InventoryMovementSourceType.Sale) {
      return 'source-badge source-badge--sale';
    }

    if (sourceType === InventoryMovementSourceType.SaleVoid) {
      return 'source-badge source-badge--sale-void';
    }

    if (sourceType === InventoryMovementSourceType.PurchaseReceipt) {
      return 'source-badge source-badge--purchase';
    }

    if (sourceType === InventoryMovementSourceType.PurchaseReceiptCancel) {
      return 'source-badge source-badge--purchase-cancel';
    }

    if (sourceType === InventoryMovementSourceType.CreditNoteReturn) {
      return 'source-badge source-badge--credit-note-return';
    }

    if (sourceType === InventoryMovementSourceType.ManualAdjustment) {
      return 'source-badge source-badge--adjustment';
    }

    return 'source-badge';
  }

  movementBusinessDateLabel(movement: InventoryMovement): string {
    return this.formatDateOnly(movement.businessDate ?? movement.createdAt);
  }

  formatBusinessTime(value: string | Date | null | undefined): string {
    return formatBusinessTimeValue(value, this.companyTimeZoneId());
  }

  formatBusinessDateTime(value: string | Date | null | undefined): string {
    return formatBusinessDateTimeValue(value, this.companyTimeZoneId());
  }

  isReversalMovement(movement: InventoryMovement): boolean {
    return movement.type === InventoryMovementType.Void
      || movement.type === InventoryMovementType.Return
      || movement.sourceType === InventoryMovementSourceType.SaleVoid
      || movement.sourceType === InventoryMovementSourceType.PurchaseReceiptCancel
      || movement.sourceType === InventoryMovementSourceType.CreditNoteReturn;
  }

  currentOperationForm(): InventoryOperationForm {
    if (this.activeOperation === 'entry') {
      return this.entryForm;
    }

    if (this.activeOperation === 'exit') {
      return this.exitForm;
    }

    return this.adjustForm;
  }

  selectedOperationStock(): InventoryStock | null {
    const productId = this.currentOperationForm().productId;
    return productId === null ? null : (this.lookupStocks().find((stock) => stock.productId === productId) ?? null);
  }

  projectedStock(): number | null {
    const stock = this.selectedOperationStock();
    const quantity = this.currentOperationForm().quantity;

    if (!stock || quantity === null || !Number.isFinite(quantity)) {
      return null;
    }

    if (this.activeOperation === 'entry') {
      return stock.quantity + quantity;
    }

    if (this.activeOperation === 'exit') {
      return stock.quantity - quantity;
    }

    return quantity;
  }

  operationLabel(kind: InventoryOperationKind): string {
    return this.operationOptions.find((option) => option.value === kind)?.label ?? kind;
  }

  operationHelpText(kind: InventoryOperationKind): string {
    if (kind === 'entry') {
      return 'Registra ingreso manual de existencias.';
    }

    if (kind === 'exit') {
      return 'Registra egreso manual cuando no proviene de una venta.';
    }

    return 'El ajuste establece el stock final del producto a un valor exacto.';
  }

  operationIcon(kind: InventoryOperationKind): string {
    if (kind === 'entry') {
      return 'pi pi-arrow-circle-down';
    }

    if (kind === 'exit') {
      return 'pi pi-arrow-circle-up';
    }

    return 'pi pi-sync';
  }

  canSubmitOperation(): boolean {
    if (this.operationIntent.pending) return !this.operationLoading();
    return !this.operationLoading() && !this.validateOperationForm(this.activeOperation, this.currentOperationForm())
      && (this.activeOperation !== 'adjust' || (!this.countLoading() && this.countSnapshot()?.productId === this.adjustForm.productId));
  }

  setOperationProduct(productId: number | null): void {
    if (this.operationLocked()) return;
    this.currentOperationForm().productId = productId;
    this.operationError.set('');
    if (this.activeOperation === 'adjust') this.loadCountSnapshot();
  }

  loadCountSnapshot(): void {
    if (this.operationLocked()) return;
    const sequence = ++this.countSequence;
    const productId = this.adjustForm.productId;
    this.countSnapshot.set(null); this.countLoading.set(false);
    if (productId === null) return;
    this.countLoading.set(true);
    this.inventoryService.getCountSnapshot(productId).subscribe({
      next: snapshot => {
        if (sequence !== this.countSequence || this.adjustForm.productId !== productId) return;
        this.countSnapshot.set(snapshot); this.countLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (sequence !== this.countSequence) return;
        this.countLoading.set(false);
        this.operationError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el saldo del conteo.'));
      },
    });
  }

  setOperationQuantity(quantity: number | null): void {
    if (this.operationLocked()) return;
    this.currentOperationForm().quantity = quantity;
    this.operationError.set('');
  }

  setOperationReference(reference: string): void {
    if (this.operationLocked()) return;
    this.currentOperationForm().reference = reference;
  }

  setOperationNotes(notes: string): void {
    if (this.operationLocked()) return;
    this.currentOperationForm().notes = notes;
  }

  private buildMovementFilters(page: number, pageSize: number): InventoryMovementFilters {
    return {
      productId: this.movementProductId,
      type: this.movementType,
      sourceType: this.movementSourceType,
      sourceId: this.movementSourceId,
      from: this.movementFrom || null,
      to: this.movementTo || null,
      userId: this.movementUserId,
      search: this.cleanText(this.movementSearch),
      page,
      pageSize,
    };
  }

  private cleanText(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private createOperationForm(): InventoryOperationForm {
    return {
      productId: null,
      quantity: null,
      reference: '',
      notes: '',
    };
  }

  private formatDateOnly(value: string | null | undefined): string {
    if (!value) {
      return '-';
    }

    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
    if (!match) {
      return '-';
    }

    return `${match[3]}/${match[2]}/${match[1]}`;
  }

  resetCurrentOperationForm(): void {
    if (this.activeOperation === 'entry') {
      this.entryForm = this.createOperationForm();
      return;
    }

    if (this.activeOperation === 'exit') {
      this.exitForm = this.createOperationForm();
      return;
    }

    this.adjustForm = this.createOperationForm();
    ++this.countSequence; this.countSnapshot.set(null); this.countLoading.set(false);
  }

  private validateOperationForm(kind: InventoryOperationKind, form: InventoryOperationForm): string {
    if (form.productId === null) {
      return 'Selecciona un producto.';
    }

    if (form.quantity === null || !Number.isFinite(form.quantity)) {
      return kind === 'adjust' ? 'Ingresa el stock final.' : 'Ingresa una cantidad.';
    }

    if (kind === 'adjust' && form.quantity < 0) {
      return 'El stock final no puede ser negativo.';
    }

    if (kind !== 'adjust' && form.quantity <= 0) {
      return 'La cantidad debe ser mayor a cero.';
    }

    return '';
  }

  private buildOperationPayload(form: InventoryOperationForm): InventoryOperationRequest {
    return {
      requestId: crypto.randomUUID(),
      productId: form.productId ?? 0,
      quantity: form.quantity ?? 0,
      reference: this.cleanText(form.reference) ?? undefined,
      notes: this.cleanText(form.notes) ?? undefined,
    };
  }

  private resolveOperationError(error: HttpErrorResponse): string {
    const code = readErrorCode(error);
    if (code === 'INVENTORY_SNAPSHOT_STALE' || code === 'INVENTORY_SNAPSHOT_REQUIRED') {
      return 'El saldo del conteo esta desactualizado. Actualiza el saldo y verifica el conteo antes de confirmar.';
    }

    if (code === 'INSUFFICIENT_STOCK') {
      return 'No hay stock suficiente para registrar la salida.';
    }

    if (code === 'PRODUCT_INACTIVE') {
      return 'El producto seleccionado está inactivo.';
    }

    if (code === 'PRODUCT_NOT_FOUND') {
      return 'El producto seleccionado no existe o no pertenece al contexto actual.';
    }

    if (code === 'INVALID_QUANTITY') {
      return 'La cantidad ingresada no es válida para esta operación.';
    }

    if (code === 'INVENTORY_CONCURRENCY_CONFLICT') {
      return 'El inventario cambió mientras se registraba la operación. Refresca e intenta nuevamente.';
    }

    return this.inventoryService.resolveError(error, 'No se pudo registrar la operación de inventario.');
  }

  private resolveStockStatus(stock: InventoryStock): StockStatus {
    if (stock.stockStatus === StockStatus.OutOfStock || stock.stockStatus === 'OutOfStock') {
      return StockStatus.OutOfStock;
    }

    if (stock.stockStatus === StockStatus.LowStock || stock.stockStatus === 'LowStock') {
      return StockStatus.LowStock;
    }

    if (stock.stockStatus === StockStatus.Ok || stock.stockStatus === 'Ok') {
      return StockStatus.Ok;
    }

    if (stock.quantity <= 0) {
      return StockStatus.OutOfStock;
    }

    return stock.minimumStock > 0 && stock.quantity <= stock.minimumStock ? StockStatus.LowStock : StockStatus.Ok;
  }
}
