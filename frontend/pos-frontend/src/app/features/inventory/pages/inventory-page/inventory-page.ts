import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
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
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { readErrorCode } from '../../../../core/utils/http-error-normalizer';
import {
  InventoryMovement,
  InventoryMovementSourceType,
  InventoryMovementType,
} from '../../models/inventory-movement.model';
import { InventoryMovementFilters } from '../../models/inventory-filters.model';
import { InventoryOperationRequest } from '../../models/inventory-operation.model';
import { InventoryStock } from '../../models/inventory-stock.model';
import { InventoryService } from '../../services/inventory.service';

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
    DatePipe,
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
  ],
  providers: [MessageService],
  templateUrl: './inventory-page.html',
  styleUrls: ['./inventory-page.scss', './inventory-operations.scss'],
})
export class InventoryPage implements OnInit {
  @ViewChild('kardexSection') private kardexSection?: ElementRef<HTMLElement>;

  private readonly inventoryService = inject(InventoryService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);
  private readonly messageService = inject(MessageService);
  readonly lowStockThreshold = 5;

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
  readonly totalProducts = computed(() => this.stocks().length);
  readonly outOfStockProducts = computed(() => this.stocks().filter((stock) => stock.quantity <= 0).length);
  readonly lowStockProducts = computed(() =>
    this.stocks().filter((stock) => stock.quantity > 0 && stock.quantity <= this.lowStockThreshold).length
  );
  readonly inactiveProducts = computed(() => this.stocks().filter((stock) => !stock.isActive).length);
  readonly totalInventoryUnits = computed(() =>
    this.stocks().reduce((total, stock) => total + stock.quantity, 0)
  );

  readonly movements = signal<InventoryMovement[]>([]);
  readonly movementsLoading = signal(false);
  readonly movementsError = signal('');
  readonly totalMovementItems = signal(0);
  readonly totalMovementPages = signal(0);
  readonly lastMovement = computed(() => this.movements()[0] ?? null);
  readonly focusedProduct = computed(() => {
    const productId = this.movementProductId;
    return productId === null ? null : (this.stocks().find((stock) => stock.productId === productId) ?? null);
  });

  readonly selectedMovement = signal<InventoryMovement | null>(null);
  readonly movementDetailLoading = signal(false);
  readonly movementDetailError = signal('');

  readonly productOptions = computed<SelectOption<number>[]>(() =>
    this.stocks().map((stock) => ({
      label: `${stock.productId} - ${stock.productName}`,
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
  ];

  readonly sourceTypeOptions: SelectOption<InventoryMovementSourceType>[] = [
    { label: 'Entrada manual', value: InventoryMovementSourceType.ManualEntry },
    { label: 'Salida manual', value: InventoryMovementSourceType.ManualExit },
    { label: 'Ajuste manual', value: InventoryMovementSourceType.ManualAdjustment },
    { label: 'Venta', value: InventoryMovementSourceType.Sale },
    { label: 'Anulación de venta', value: InventoryMovementSourceType.SaleVoid },
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
  entryForm: InventoryOperationForm = this.createOperationForm();
  exitForm: InventoryOperationForm = this.createOperationForm();
  adjustForm: InventoryOperationForm = this.createOperationForm();

  ngOnInit(): void {
    if (this.canReadInventory()) {
      this.refreshAll();
    }
  }

  refreshAll(): void {
    this.loadStocks();
    this.loadMovements(1, this.movementRows);
  }

  loadStocks(): void {
    this.stockLoading.set(true);
    this.stockError.set('');

    this.inventoryService
      .getStocks(this.cleanText(this.stockSearch), this.stockProductId, this.stockOnlyPositive)
      .subscribe({
        next: (stocks) => {
          this.stocks.set(stocks);
          this.stockLoading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.stockLoading.set(false);
          this.stockError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el stock actual.'));
        },
      });
  }

  applyStockFilters(): void {
    this.loadStocks();
  }

  clearStockFilters(): void {
    this.stockSearch = '';
    this.stockProductId = null;
    this.stockOnlyPositive = false;
    this.loadStocks();
  }

  loadMovements(page: number, pageSize: number): void {
    this.movementsLoading.set(true);
    this.movementsError.set('');

    this.inventoryService.getMovements(this.buildMovementFilters(page, pageSize)).subscribe({
      next: (result) => {
        this.movements.set(result.items);
        this.totalMovementItems.set(result.totalItems);
        this.totalMovementPages.set(result.totalPages);
        this.movementRows = result.pageSize;
        this.movementFirst = (result.page - 1) * result.pageSize;
        this.movementsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
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
    const form = this.currentOperationForm();
    const validationError = this.validateOperationForm(this.activeOperation, form);

    this.operationError.set('');
    this.operationResult.set(null);

    if (validationError) {
      this.operationError.set(validationError);
      return;
    }

    const payload = this.buildOperationPayload(form);
    this.operationLoading.set(true);

    const request =
      this.activeOperation === 'entry'
        ? this.inventoryService.registerEntry(payload)
        : this.activeOperation === 'exit'
          ? this.inventoryService.registerExit(payload)
          : this.inventoryService.registerAdjustment(payload);

    request.subscribe({
      next: (movement) => {
        this.operationLoading.set(false);
        this.operationResult.set(movement);
        this.messageService.add({
          severity: 'success',
          summary: 'Inventario actualizado',
          detail: `${this.operationLabel(this.activeOperation)} registrada correctamente.`,
        });
        this.resetCurrentOperationForm();
        this.movementProductId = movement.productId;
        this.movementType = movement.type;
        this.loadStocks();
        this.applyMovementFilters();
      },
      error: (error: HttpErrorResponse) => {
        this.operationLoading.set(false);
        this.operationError.set(this.resolveOperationError(error));
      },
    });
  }

  openMovementDetail(movement: InventoryMovement): void {
    this.movementDetailVisible = true;
    this.selectedMovement.set(null);
    this.movementDetailError.set('');
    this.movementDetailLoading.set(true);

    this.inventoryService.getMovementById(movement.id).subscribe({
      next: (detail) => {
        this.selectedMovement.set(detail);
        this.movementDetailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.movementDetailLoading.set(false);
        this.movementDetailError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el movimiento.'));
      },
    });
  }

  onMovementDetailVisibleChange(visible: boolean): void {
    this.movementDetailVisible = visible;
    if (!visible) {
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

  stockStatusLabel(quantity: number): string {
    return quantity > 0 ? 'Disponible' : 'Sin stock';
  }

  stockStatusSeverity(quantity: number): 'success' | 'danger' {
    return quantity > 0 ? 'success' : 'danger';
  }

  stockRiskLabel(stock: InventoryStock): string {
    if (stock.quantity <= 0) {
      return 'Crítico';
    }

    if (stock.quantity <= this.lowStockThreshold) {
      return 'Bajo';
    }

    return 'Normal';
  }

  stockRiskClass(stock: InventoryStock): string {
    const classes = ['stock-risk'];

    if (stock.quantity <= 0) {
      classes.push('stock-risk--critical');
    } else if (stock.quantity <= this.lowStockThreshold) {
      classes.push('stock-risk--warning');
    }

    if (!stock.isActive) {
      classes.push('stock-risk--inactive');
    }

    return classes.join(' ');
  }

  stockRowClass(stock: InventoryStock): string {
    const classes: string[] = [];

    if (stock.quantity <= 0) {
      classes.push('row-critical');
    } else if (stock.quantity <= this.lowStockThreshold) {
      classes.push('row-warning');
    }

    if (!stock.isActive) {
      classes.push('row-inactive');
    }

    return classes.join(' ');
  }

  movementSeverity(type: InventoryMovementType): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined {
    if (type === InventoryMovementType.Entry) {
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

    if (sourceType === InventoryMovementSourceType.ManualAdjustment) {
      return 'source-badge source-badge--adjustment';
    }

    return 'source-badge';
  }

  isReversalMovement(movement: InventoryMovement): boolean {
    return movement.type === InventoryMovementType.Void || movement.sourceType === InventoryMovementSourceType.SaleVoid;
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
    return productId === null ? null : (this.stocks().find((stock) => stock.productId === productId) ?? null);
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
    return !this.operationLoading() && !this.validateOperationForm(this.activeOperation, this.currentOperationForm());
  }

  setOperationProduct(productId: number | null): void {
    this.currentOperationForm().productId = productId;
    this.operationError.set('');
  }

  setOperationQuantity(quantity: number | null): void {
    this.currentOperationForm().quantity = quantity;
    this.operationError.set('');
  }

  setOperationReference(reference: string): void {
    this.currentOperationForm().reference = reference;
  }

  setOperationNotes(notes: string): void {
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
  }

  private validateOperationForm(kind: InventoryOperationKind, form: InventoryOperationForm): string {
    if (form.productId === null) {
      return 'Selecciona un producto.';
    }

    const stock = this.stocks().find((item) => item.productId === form.productId);
    if (stock && !stock.isActive) {
      return 'El producto seleccionado está inactivo.';
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
      productId: form.productId ?? 0,
      quantity: form.quantity ?? 0,
      reference: this.cleanText(form.reference) ?? undefined,
      notes: this.cleanText(form.notes) ?? undefined,
    };
  }

  private resolveOperationError(error: HttpErrorResponse): string {
    const code = readErrorCode(error);

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
}
