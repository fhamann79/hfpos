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
import { ToastModule } from 'primeng/toast';
import { ToolbarModule } from 'primeng/toolbar';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { InventoryMovement, InventoryMovementDetail } from '../../models/inventory-movement.model';
import { InventoryMovementFilters } from '../../models/inventory-filters.model';
import { InventoryStock } from '../../models/inventory-stock.model';
import { InventoryService } from '../../services/inventory.service';

interface SelectOption<T> {
  label: string;
  value: T;
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
    ToastModule,
    ToolbarModule,
  ],
  templateUrl: './inventory-page.html',
  styleUrl: './inventory-page.scss',
})
export class InventoryPage implements OnInit {
  private readonly inventoryService = inject(InventoryService);
  private readonly permissionService = inject(PermissionService);

  readonly canReadStock = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryStocksRead));
  readonly canReadMovements = computed(() => this.permissionService.hasPermission(PERMISSIONS.inventoryMovementsRead));

  readonly stocks = signal<InventoryStock[]>([]);
  readonly stockLoading = signal(false);
  readonly stockError = signal('');

  readonly movements = signal<InventoryMovement[]>([]);
  readonly movementsLoading = signal(false);
  readonly movementsError = signal('');
  readonly totalMovementRecords = signal(0);

  readonly selectedMovement = signal<InventoryMovementDetail | null>(null);
  readonly movementDetailLoading = signal(false);
  readonly movementDetailError = signal('');

  readonly productOptions = computed<SelectOption<number>[]>(() =>
    this.stocks()
      .map((stock) => ({ label: stock.productName, value: stock.productId }))
      .sort((a, b) => a.label.localeCompare(b.label))
  );

  readonly movementTypeOptions: SelectOption<string>[] = [
    { label: 'Entrada', value: 'ENTRY' },
    { label: 'Salida', value: 'EXIT' },
    { label: 'Ajuste', value: 'ADJUSTMENT' },
    { label: 'Venta', value: 'SALE' },
    { label: 'Anulación', value: 'VOID' },
  ];

  selectedProductId: number | null = null;
  selectedMovementType: string | null = null;
  fromDate = '';
  toDate = '';
  movementDetailVisible = false;
  movementFirst = 0;
  movementRows = 10;

  ngOnInit(): void {
    this.refreshAll();
  }

  refreshAll(): void {
    if (this.canReadStock()) {
      this.loadStocks();
    }

    if (this.canReadMovements()) {
      this.loadMovements(1, this.movementRows);
    }
  }

  loadStocks(): void {
    this.stockLoading.set(true);
    this.stockError.set('');

    this.inventoryService.getStocks().subscribe({
      next: (stocks) => {
        this.stocks.set(stocks.sort((a, b) => a.productName.localeCompare(b.productName)));
        this.stockLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.stockLoading.set(false);
        this.stockError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el stock.'));
      },
    });
  }

  loadMovements(page: number, pageSize: number): void {
    this.movementsLoading.set(true);
    this.movementsError.set('');

    this.inventoryService.getMovements(this.buildFilters(), page, pageSize).subscribe({
      next: (result) => {
        this.movements.set(result.items);
        this.totalMovementRecords.set(result.totalRecords);
        this.movementsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.movementsLoading.set(false);
        this.movementsError.set(this.inventoryService.resolveError(error, 'No se pudieron cargar los movimientos.'));
      },
    });
  }

  onMovementsLazyLoad(event: TableLazyLoadEvent): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.movementRows;

    this.movementFirst = first;
    this.movementRows = rows;
    this.loadMovements(Math.floor(first / rows) + 1, rows);
  }

  applyFilters(): void {
    this.movementFirst = 0;
    this.loadMovements(1, this.movementRows);
  }

  clearFilters(): void {
    this.selectedProductId = null;
    this.selectedMovementType = null;
    this.fromDate = '';
    this.toDate = '';
    this.applyFilters();
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
        this.movementDetailError.set(this.inventoryService.resolveError(error, 'No se pudo cargar el detalle.'));
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

  movementTypeLabel(type: string): string {
    const normalized = type.toUpperCase();
    return this.movementTypeOptions.find((option) => option.value === normalized)?.label ?? type;
  }

  movementSeverity(type: string): 'success' | 'secondary' | 'info' | 'warn' | 'danger' | 'contrast' | undefined {
    const normalized = type.toUpperCase();

    if (normalized === 'ENTRY' || normalized === 'VOID') {
      return 'success';
    }

    if (normalized === 'EXIT' || normalized === 'SALE') {
      return 'danger';
    }

    if (normalized === 'ADJUSTMENT') {
      return 'warn';
    }

    return 'secondary';
  }

  private buildFilters(): InventoryMovementFilters {
    return {
      productId: this.selectedProductId,
      type: this.selectedMovementType,
      fromDate: this.fromDate || null,
      toDate: this.toDate || null,
    };
  }
}
