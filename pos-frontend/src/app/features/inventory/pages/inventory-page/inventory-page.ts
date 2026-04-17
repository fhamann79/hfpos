import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { ToolbarModule } from 'primeng/toolbar';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  InventoryMovement,
  InventoryMovementSourceType,
  InventoryMovementType,
} from '../../models/inventory-movement.model';
import { InventoryMovementFilters } from '../../models/inventory-filters.model';
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
    InputTextModule,
    SelectModule,
    MessageModule,
    TagModule,
    ToggleSwitchModule,
    ToolbarModule,
  ],
  templateUrl: './inventory-page.html',
  styleUrl: './inventory-page.scss',
})
export class InventoryPage implements OnInit {
  @ViewChild('kardexSection') private kardexSection?: ElementRef<HTMLElement>;

  private readonly inventoryService = inject(InventoryService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);
  readonly lowStockThreshold = 5;

  readonly canReadInventory = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryRead));
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
}
