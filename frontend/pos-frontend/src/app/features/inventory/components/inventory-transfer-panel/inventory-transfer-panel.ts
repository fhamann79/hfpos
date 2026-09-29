import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TextareaModule } from 'primeng/textarea';
import { AuthStore } from '../../../../core/stores/auth.store';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import { InventoryStock } from '../../models/inventory-stock.model';
import {
  InventoryTransferCreateRequest,
  InventoryTransferDestination,
  InventoryTransferDetail,
  InventoryTransferListItem,
} from '../../models/inventory-transfer.model';
import { InventoryService } from '../../services/inventory.service';

interface TransferLine {
  id: number;
  productId: number | null;
  quantity: number | null;
}

@Component({
  selector: 'app-inventory-transfer-panel',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ButtonModule, DialogModule, InputNumberModule,
    InputTextModule, MessageModule, SelectModule, TableModule, TextareaModule,
  ],
  templateUrl: './inventory-transfer-panel.html',
  styleUrl: './inventory-transfer-panel.scss',
})
export class InventoryTransferPanel implements OnInit {
  @Input() stocks: InventoryStock[] = [];
  @Input() canWrite = false;
  @Output() transferred = new EventEmitter<void>();

  private readonly inventory = inject(InventoryService);
  readonly auth = inject(AuthStore);
  readonly destinations = signal<InventoryTransferDestination[]>([]);
  readonly destinationsError = signal('');
  readonly transfers = signal<InventoryTransferListItem[]>([]);
  readonly totalItems = signal(0);
  readonly historyLoading = signal(false);
  readonly historyError = signal('');
  readonly submitLoading = signal(false);
  readonly submitError = signal('');
  readonly submitSuccess = signal('');
  readonly detail = signal<InventoryTransferDetail | null>(null);
  readonly detailError = signal('');
  readonly detailLoading = signal(false);

  destinationId: number | null = null;
  reference = '';
  notes = '';
  lines: TransferLine[] = [{ id: 1, productId: null, quantity: null }];
  private nextLineId = 2;
  requestId = crypto.randomUUID();
  pageSize = 25;
  first = 0;
  from = '';
  to = '';
  search = '';
  detailVisible = false;

  ngOnInit(): void {
    if (this.canWrite) this.loadDestinations();
    this.loadTransfers(1, this.pageSize);
  }

  get productOptions(): { label: string; value: number }[] {
    return this.stocks.filter(stock => stock.quantity > 0).map(stock => ({
      label: `${stock.productName}${stock.isActive ? '' : ' (Inactivo)'} · ${stock.quantity} disp.`,
      value: stock.productId,
    }));
  }

  get validationMessage(): string | null {
    if (!this.canWrite) return 'No tienes permiso para transferir inventario.';
    if (!this.destinationId || this.destinationId === this.auth.establishmentId()
      || !this.destinations().some(item => item.id === this.destinationId))
      return 'Selecciona otro establecimiento activo como destino.';
    if (!this.lines.length) return 'Agrega al menos un producto.';
    const ids = this.lines.map(line => line.productId);
    if (ids.some(id => !id)) return 'Selecciona un producto en cada línea.';
    if (new Set(ids).size !== ids.length) return 'No repitas productos en la transferencia.';
    for (const line of this.lines) {
      const available = this.stocks.find(stock => stock.productId === line.productId)?.quantity ?? 0;
      if (!line.quantity || !Number.isFinite(line.quantity) || line.quantity <= 0)
        return 'Cada cantidad debe ser mayor a cero.';
      if (line.quantity > available) return 'La cantidad supera el stock disponible en origen.';
    }
    if (this.reference.trim().length > 100 || this.notes.trim().length > 500)
      return 'Revisa la longitud de referencia o notas.';
    return null;
  }

  stockFor(productId: number | null): number {
    return this.stocks.find(stock => stock.productId === productId)?.quantity ?? 0;
  }

  onDraftChanged(): void {
    this.requestId = crypto.randomUUID();
    this.submitError.set('');
    this.submitSuccess.set('');
  }

  addLine(): void {
    this.lines = [...this.lines, { id: this.nextLineId++, productId: null, quantity: null }];
    this.onDraftChanged();
  }

  removeLine(id: number): void {
    this.lines = this.lines.filter(line => line.id !== id);
    this.onDraftChanged();
  }

  resetDraft(): void {
    this.destinationId = null;
    this.reference = '';
    this.notes = '';
    this.lines = [{ id: this.nextLineId++, productId: null, quantity: null }];
    this.onDraftChanged();
  }

  submit(): void {
    if (this.submitLoading() || this.validationMessage) {
      this.submitError.set(this.validationMessage ?? '');
      return;
    }
    const payload: InventoryTransferCreateRequest = {
      destinationEstablishmentId: this.destinationId!,
      requestId: this.requestId,
      reference: this.reference.trim() || null,
      notes: this.notes.trim() || null,
      items: this.lines.map(line => ({ productId: line.productId!, quantity: line.quantity! })),
    };
    this.submitLoading.set(true);
    this.submitError.set('');
    this.inventory.createTransfer(payload).subscribe({
      next: transfer => {
        this.submitLoading.set(false);
        this.detail.set(transfer);
        this.detailVisible = true;
        this.resetDraft();
        this.submitSuccess.set(transfer.wasAlreadyProcessed
          ? `La petición ya estaba registrada como transferencia #${transfer.id}; no se movió stock otra vez.`
          : `Transferencia #${transfer.id} registrada.`);
        this.transferred.emit();
        this.loadTransfers(1, this.pageSize);
      },
      error: (error: HttpErrorResponse) => {
        this.submitLoading.set(false);
        this.submitError.set(this.inventory.resolveError(error, 'No se pudo registrar la transferencia.'));
      },
    });
  }

  loadDestinations(): void {
    this.destinationsError.set('');
    this.inventory.getTransferDestinations().subscribe({
      next: destinations => this.destinations.set(destinations.filter(
        item => item.id !== this.auth.establishmentId())),
      error: (error: HttpErrorResponse) =>
        this.destinationsError.set(this.inventory.resolveError(error, 'No se pudieron cargar los destinos.')),
    });
  }

  loadTransfers(page: number, pageSize: number): void {
    this.historyLoading.set(true);
    this.historyError.set('');
    this.inventory.getTransfers({
      page, pageSize, from: this.from || null, to: this.to || null, search: this.search.trim() || null,
    }).subscribe({
      next: result => {
        this.transfers.set(result.items);
        this.totalItems.set(result.totalItems);
        this.pageSize = result.pageSize;
        this.first = (result.page - 1) * result.pageSize;
        this.historyLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.historyLoading.set(false);
        this.historyError.set(this.inventory.resolveError(error, 'No se pudo cargar el historial.'));
      },
    });
  }

  onLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize;
    this.loadTransfers(Math.floor((event.first ?? 0) / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.loadTransfers(1, this.pageSize);
  }

  openDetail(id: number): void {
    this.detailVisible = true;
    this.detail.set(null);
    this.detailError.set('');
    this.detailLoading.set(true);
    this.inventory.getTransferById(id).subscribe({
      next: detail => {
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(this.inventory.resolveError(error, 'No se pudo cargar la transferencia.'));
      },
    });
  }

  formatDate(value: string): string {
    return formatBusinessDateTime(value, this.auth.companyTimeZoneId());
  }
}
