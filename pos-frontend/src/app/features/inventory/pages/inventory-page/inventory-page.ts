import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
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
    DialogModule,
    InputTextModule,
    SelectModule,
    MessageModule,
    TagModule,
    ToolbarModule,
  ],
  templateUrl: './inventory-page.html',
  styleUrl: './inventory-page.scss',
})
export class InventoryPage implements OnInit {
  private readonly inventoryService = inject(InventoryService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);

  readonly canReadInventory = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryRead));
  readonly contextItems = computed<ContextItem[]>(() => [
    { label: 'CompanyId', value: this.authStore.companyId() },
    { label: 'EstablishmentId', value: this.authStore.establishmentId() },
    { label: 'EmissionPointId', value: this.authStore.emissionPointId() },
  ]);

  readonly stocks = signal<InventoryStock[]>([]);
  readonly stockLoading = signal(false);
  readonly stockError = signal('');

  readonly movements = signal<InventoryMovement[]>([]);
  readonly movementsLoading = signal(false);
  readonly movementsError = signal('');
  readonly totalMovementItems = signal(0);
  readonly totalMovementPages = signal(0);

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
    { label: 'Initial', value: InventoryMovementType.Initial },
    { label: 'Entry', value: InventoryMovementType.Entry },
    { label: 'Exit', value: InventoryMovementType.Exit },
    { label: 'Adjustment', value: InventoryMovementType.Adjustment },
    { label: 'Sale', value: InventoryMovementType.Sale },
    { label: 'Void', value: InventoryMovementType.Void },
  ];

  readonly sourceTypeOptions: SelectOption<InventoryMovementSourceType>[] = [
    { label: 'ManualEntry', value: InventoryMovementSourceType.ManualEntry },
    { label: 'ManualExit', value: InventoryMovementSourceType.ManualExit },
    { label: 'ManualAdjustment', value: InventoryMovementSourceType.ManualAdjustment },
    { label: 'Sale', value: InventoryMovementSourceType.Sale },
    { label: 'SaleVoid', value: InventoryMovementSourceType.SaleVoid },
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
    return InventoryMovementType[type] ?? String(type);
  }

  sourceTypeName(sourceType: InventoryMovementSourceType): string {
    return InventoryMovementSourceType[sourceType] ?? String(sourceType);
  }

  statusSeverity(isActive: boolean): 'success' | 'danger' {
    return isActive ? 'success' : 'danger';
  }

  movementSeverity(type: InventoryMovementType): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined {
    if (type === InventoryMovementType.Entry || type === InventoryMovementType.Void) {
      return 'success';
    }

    if (type === InventoryMovementType.Exit || type === InventoryMovementType.Sale) {
      return 'danger';
    }

    if (type === InventoryMovementType.Adjustment) {
      return 'warn';
    }

    return 'secondary';
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
