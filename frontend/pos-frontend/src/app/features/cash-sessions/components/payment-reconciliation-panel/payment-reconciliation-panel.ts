import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import {
  CreatePaymentSettlementRequest,
  PaymentMethodActivity,
  PaymentReconciliation,
  PaymentSettlement,
  SettlementPaymentMethod,
} from '../../models/payment-settlement.model';
import { PaymentSettlementService } from '../../services/payment-settlement.service';

@Component({
  selector: 'app-payment-reconciliation-panel',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, FormsModule, ButtonModule, DialogModule, InputTextModule,
    MessageModule, SelectModule, TableModule, TagModule, TextareaModule],
  templateUrl: './payment-reconciliation-panel.html',
  styleUrl: './payment-reconciliation-panel.scss',
})
export class PaymentReconciliationPanel implements OnInit {
  @Input() canWrite = false;
  private readonly service = inject(PaymentSettlementService);

  readonly overview = signal<PaymentReconciliation | null>(null);
  readonly overviewLoading = signal(false);
  readonly overviewError = signal('');
  readonly history = signal<PaymentSettlement[]>([]);
  readonly historyTotal = signal(0);
  readonly historyLoading = signal(false);
  readonly historyError = signal('');
  readonly saving = signal(false);
  readonly formError = signal('');
  readonly detail = signal<PaymentSettlement | null>(null);
  readonly detailError = signal('');

  readonly methodOptions = [
    { label: 'Tarjeta', value: SettlementPaymentMethod.Card },
    { label: 'Transferencia', value: SettlementPaymentMethod.Transfer },
    { label: 'Otros', value: SettlementPaymentMethod.Other },
  ];

  businessDate = '';
  historyFrom = '';
  historyTo = '';
  historyMethod: SettlementPaymentMethod | null = null;
  first = 0;
  rows = 15;
  settleDialogVisible = false;
  detailDialogVisible = false;
  selectedMethod: PaymentMethodActivity | null = null;
  settledAmount: number | null = null;
  reference = '';
  notes = '';
  private requestId = '';

  ngOnInit(): void {
    this.loadOverview();
    this.loadHistory();
  }

  loadOverview(): void {
    this.overviewLoading.set(true);
    this.overviewError.set('');
    this.service.getReconciliation(this.businessDate || undefined).subscribe({
      next: (value) => {
        this.overview.set(value);
        this.businessDate = value.businessDate;
        this.overviewLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.overviewLoading.set(false);
        this.overviewError.set(resolveHttpErrorMessage(error, 'No se pudo consultar la conciliación.'));
      },
    });
  }

  changeDate(): void {
    this.settleDialogVisible = false;
    this.selectedMethod = null;
    this.requestId = '';
    if (this.businessDate) this.loadOverview();
  }

  loadHistory(page = 1, pageSize = this.rows): void {
    this.historyLoading.set(true);
    this.historyError.set('');
    this.service.getAll({
      from: this.historyFrom,
      to: this.historyTo,
      paymentMethod: this.historyMethod,
      page,
      pageSize,
    }).subscribe({
      next: (result) => {
        this.history.set(result.items);
        this.historyTotal.set(result.totalItems);
        this.rows = result.pageSize;
        this.first = (result.page - 1) * result.pageSize;
        this.historyLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.historyLoading.set(false);
        this.historyError.set(resolveHttpErrorMessage(error, 'No se pudo cargar el historial.'));
      },
    });
  }

  onHistoryLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    const first = event.first ?? this.first;
    this.loadHistory(Math.floor(first / rows) + 1, rows);
  }

  applyHistoryFilters(): void {
    this.first = 0;
    this.loadHistory();
  }

  openSettlement(method: PaymentMethodActivity): void {
    if (!this.canWrite || !method.canSettle || this.saving()) return;
    this.selectedMethod = method;
    this.settledAmount = method.netPaymentAmount;
    this.reference = '';
    this.notes = '';
    this.requestId = crypto.randomUUID();
    this.formError.set('');
    this.settleDialogVisible = true;
  }

  draftChanged(): void {
    this.requestId = crypto.randomUUID();
  }

  closeSettlement(): void {
    if (this.saving()) return;
    this.settleDialogVisible = false;
    this.selectedMethod = null;
    this.formError.set('');
  }

  get differencePreview(): number | null {
    if (this.settledAmount === null || !this.selectedMethod) return null;
    return Math.round((this.settledAmount - this.selectedMethod.netPaymentAmount) * 100) / 100;
  }

  confirmSettlement(): void {
    const overview = this.overview();
    const method = this.selectedMethod;
    if (!this.canWrite || this.saving() || !overview || !method || !method.canSettle) return;
    const amount = this.settledAmount;
    if (amount === null || !Number.isFinite(amount) || Math.abs(amount) > 9999999999999999.99
        || Math.abs(Math.round(amount * 100) - amount * 100) > 0.0000001) {
      this.formError.set('Ingresa un monto válido con máximo dos decimales.');
      return;
    }
    if (this.reference.trim().length > 150 || this.notes.trim().length > 500) {
      this.formError.set('La referencia o las notas superan la longitud permitida.');
      return;
    }

    const request: CreatePaymentSettlementRequest = {
      requestId: this.requestId,
      businessDate: overview.businessDate,
      paymentMethod: method.paymentMethod,
      settledAmount: amount,
      reference: this.reference.trim() || null,
      notes: this.notes.trim() || null,
    };
    this.saving.set(true);
    this.formError.set('');
    this.service.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.settleDialogVisible = false;
        this.selectedMethod = null;
        this.requestId = crypto.randomUUID();
        this.loadOverview();
        this.loadHistory();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.formError.set(resolveHttpErrorMessage(error, 'No se pudo registrar la conciliación.'));
      },
    });
  }

  openDetail(id: number): void {
    this.detail.set(null);
    this.detailError.set('');
    this.detailDialogVisible = true;
    this.service.getById(id).subscribe({
      next: (value) => this.detail.set(value),
      error: (error: HttpErrorResponse) =>
        this.detailError.set(resolveHttpErrorMessage(error, 'No se pudo cargar el detalle.')),
    });
  }

  methodLabel(method: SettlementPaymentMethod): string {
    return method === SettlementPaymentMethod.Cash ? 'Efectivo'
      : method === SettlementPaymentMethod.Card ? 'Tarjeta'
      : method === SettlementPaymentMethod.Transfer ? 'Transferencia' : 'Otros';
  }

  differenceStatus(value: number): string {
    return value === 0 ? 'Conciliado' : 'Diferencia';
  }
}
