import { CommonModule, CurrencyPipe } from '@angular/common';
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
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  formatBusinessDateTime as formatBusinessDateTimeValue,
  formatBusinessTime as formatBusinessTimeValue,
} from '../../../../core/utils/business-date-format';
import { readErrorCode, resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import {
  CashMovement,
  CashMovementType,
  CashSession,
  CashSessionListItem,
  CashSessionStatus,
} from '../../models/cash-session.model';
import { CashSessionService } from '../../services/cash-session.service';

interface SelectOption<T> {
  label: string;
  value: T;
}

@Component({
  selector: 'app-cash-sessions-page',
  standalone: true,
  imports: [
    CommonModule,
    CurrencyPipe,
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
  templateUrl: './cash-sessions-page.html',
  styleUrl: './cash-sessions-page.scss',
})
export class CashSessionsPage implements OnInit {
  private readonly cashSessionService = inject(CashSessionService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);
  private readonly messageService = inject(MessageService);

  readonly currentSession = signal<CashSession | null>(null);
  readonly sessions = signal<CashSessionListItem[]>([]);
  readonly selectedSession = signal<CashSession | null>(null);
  readonly currentLoading = signal(false);
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly saving = signal(false);
  readonly errorMessage = signal('');
  readonly detailError = signal('');
  readonly formError = signal('');

  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.cashSessionsWrite));
  readonly companyTimeZoneId = computed(() => this.authStore.companyTimeZoneId());

  readonly totalOpenSessions = computed(() => this.sessions().filter((session) => session.status === CashSessionStatus.Open).length);
  readonly totalClosedSessions = computed(() => this.sessions().filter((session) => session.status === CashSessionStatus.Closed).length);

  readonly statusOptions: SelectOption<CashSessionStatus>[] = [
    { label: 'Abiertas', value: CashSessionStatus.Open },
    { label: 'Cerradas', value: CashSessionStatus.Closed },
  ];

  readonly movementOptions: SelectOption<CashMovementType>[] = [
    { label: 'Ingreso de efectivo', value: CashMovementType.CashIn },
    { label: 'Egreso de efectivo', value: CashMovementType.CashOut },
  ];

  from = '';
  to = '';
  status: CashSessionStatus | null = null;
  userId: number | null = null;
  openDialogVisible = false;
  movementDialogVisible = false;
  closeDialogVisible = false;
  detailDialogVisible = false;
  openingAmount: number | null = 0;
  openingNotes = '';
  movementType: CashMovementType = CashMovementType.CashIn;
  movementAmount: number | null = null;
  movementReason = '';
  countedCashAmount: number | null = null;
  closingNotes = '';

  ngOnInit(): void {
    this.refreshAll();
  }

  refreshAll(): void {
    this.loadCurrent();
    this.loadSessions();
  }

  loadCurrent(): void {
    this.currentLoading.set(true);

    this.cashSessionService.getCurrent().subscribe({
      next: (session) => {
        this.currentSession.set(session);
        this.currentLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.currentLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Caja',
          detail: this.resolveCashError(error, 'No se pudo consultar la caja actual.'),
        });
      },
    });
  }

  loadSessions(): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.cashSessionService
      .getAll({
        from: this.from,
        to: this.to,
        status: this.status,
        userId: this.userId,
      })
      .subscribe({
        next: (sessions) => {
          this.sessions.set(sessions);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          this.errorMessage.set(this.resolveCashError(error, 'No se pudo cargar el historial de caja.'));
        },
      });
  }

  clearFilters(): void {
    this.from = '';
    this.to = '';
    this.status = null;
    this.userId = null;
    this.loadSessions();
  }

  openCashDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.openingAmount = 0;
    this.openingNotes = '';
    this.formError.set('');
    this.openDialogVisible = true;
  }

  closeOpenDialog(): void {
    this.openDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmOpen(): void {
    if (!this.canWrite()) {
      return;
    }

    const amount = this.parseAmount(this.openingAmount);
    if (amount === null || amount < 0) {
      this.formError.set('El monto inicial debe ser mayor o igual a 0.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');

    this.cashSessionService.open({
      openingAmount: amount,
      openingNotes: this.normalizeOptionalText(this.openingNotes),
    }).subscribe({
      next: (session) => {
        this.saving.set(false);
        this.openDialogVisible = false;
        this.currentSession.set(session);
        this.messageService.add({ severity: 'success', summary: 'Caja abierta', detail: 'La caja quedó lista para vender.' });
        this.loadSessions();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.formError.set(this.resolveCashError(error, 'No se pudo abrir la caja.'));
      },
    });
  }

  openMovementDialog(type: CashMovementType): void {
    if (!this.canWrite() || !this.currentSession()) {
      return;
    }

    this.movementType = type;
    this.movementAmount = null;
    this.movementReason = '';
    this.formError.set('');
    this.movementDialogVisible = true;
  }

  closeMovementDialog(): void {
    this.movementDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmMovement(): void {
    const session = this.currentSession();
    if (!this.canWrite() || !session) {
      return;
    }

    const amount = this.parseAmount(this.movementAmount);
    const reason = this.movementReason.trim();
    if (amount === null || amount <= 0) {
      this.formError.set('El monto del movimiento debe ser mayor a 0.');
      return;
    }

    if (!reason || reason.length > 300) {
      this.formError.set('Ingresa una razón de movimiento válida.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');

    this.cashSessionService.addMovement(session.id, {
      type: this.movementType,
      amount,
      reason,
    }).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.movementDialogVisible = false;
        this.currentSession.set(updated);
        this.messageService.add({ severity: 'success', summary: 'Movimiento registrado', detail: 'La caja fue actualizada.' });
        this.loadSessions();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.formError.set(this.resolveCashError(error, 'No se pudo registrar el movimiento.'));
      },
    });
  }

  openCloseDialog(): void {
    const session = this.currentSession();
    if (!this.canWrite() || !session) {
      return;
    }

    this.countedCashAmount = session.expectedCashAmount;
    this.closingNotes = '';
    this.formError.set('');
    this.closeDialogVisible = true;
  }

  closeCloseDialog(): void {
    this.closeDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmClose(): void {
    const session = this.currentSession();
    if (!this.canWrite() || !session) {
      return;
    }

    const countedAmount = this.parseAmount(this.countedCashAmount);
    if (countedAmount === null || countedAmount < 0) {
      this.formError.set('El efectivo contado debe ser mayor o igual a 0.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');

    this.cashSessionService.close(session.id, {
      countedCashAmount: countedAmount,
      closingNotes: this.normalizeOptionalText(this.closingNotes),
    }).subscribe({
      next: (closed) => {
        this.saving.set(false);
        this.closeDialogVisible = false;
        this.currentSession.set(null);
        this.selectedSession.set(closed);
        this.detailDialogVisible = true;
        this.messageService.add({ severity: 'success', summary: 'Caja cerrada', detail: 'El cierre fue guardado correctamente.' });
        this.loadSessions();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.formError.set(this.resolveCashError(error, 'No se pudo cerrar la caja.'));
      },
    });
  }

  openDetail(session: CashSessionListItem): void {
    this.detailDialogVisible = true;
    this.detailLoading.set(true);
    this.detailError.set('');
    this.selectedSession.set(null);

    this.cashSessionService.getById(session.id).subscribe({
      next: (detail) => {
        this.selectedSession.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(this.resolveCashError(error, 'No se pudo cargar el detalle de caja.'));
      },
    });
  }

  closeDetailDialog(): void {
    this.detailDialogVisible = false;
    this.selectedSession.set(null);
    this.detailError.set('');
  }

  isOpen(session: CashSession | CashSessionListItem): boolean {
    return session.status === CashSessionStatus.Open;
  }

  statusLabel(status: CashSessionStatus): string {
    if (status === CashSessionStatus.Open) {
      return 'Abierta';
    }

    if (status === CashSessionStatus.Closed) {
      return 'Cerrada';
    }

    return String(status);
  }

  statusSeverity(status: CashSessionStatus): 'success' | 'secondary' {
    return status === CashSessionStatus.Open ? 'success' : 'secondary';
  }

  movementLabel(type: CashMovementType): string {
    return type === CashMovementType.CashIn ? 'Ingreso' : 'Egreso';
  }

  movementSeverity(type: CashMovementType): 'success' | 'danger' {
    return type === CashMovementType.CashIn ? 'success' : 'danger';
  }

  movementAmountClass(movement: CashMovement): string {
    return movement.type === CashMovementType.CashIn ? 'positive-amount' : 'negative-amount';
  }

  differenceClass(value: number | null | undefined): string {
    if (value === null || value === undefined || value === 0) {
      return 'neutral-amount';
    }

    return value > 0 ? 'positive-amount' : 'negative-amount';
  }

  formatBusinessTime(value: string | Date | null | undefined): string {
    return formatBusinessTimeValue(value, this.companyTimeZoneId());
  }

  formatBusinessDateTime(value: string | Date | null | undefined): string {
    return formatBusinessDateTimeValue(value, this.companyTimeZoneId());
  }

  formatDateOnly(value: string | null | undefined): string {
    if (!value) {
      return '-';
    }

    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
    if (!match) {
      return '-';
    }

    return `${match[3]}/${match[2]}/${match[1]}`;
  }

  private parseAmount(value: number | string | null): number | null {
    if (value === null || value === '') {
      return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  private normalizeOptionalText(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  private resolveCashError(error: HttpErrorResponse, fallback: string): string {
    switch (readErrorCode(error)) {
      case 'CASH_SESSION_ALREADY_OPEN':
        return 'Ya tienes una caja abierta para este punto de emisión.';
      case 'CASH_SESSION_REQUIRED':
        return 'Debes abrir caja antes de vender.';
      case 'CASH_SESSION_NOT_OPEN':
        return 'La caja no está abierta.';
      case 'CASH_SESSION_ALREADY_CLOSED':
        return 'La caja ya está cerrada.';
      case 'CASH_SESSION_OPENING_AMOUNT_INVALID':
        return 'El monto inicial debe ser mayor o igual a 0.';
      case 'CASH_SESSION_COUNTED_AMOUNT_INVALID':
        return 'El efectivo contado debe ser mayor o igual a 0.';
      case 'CASH_MOVEMENT_AMOUNT_INVALID':
        return 'El monto del movimiento debe ser mayor a 0.';
      case 'CASH_MOVEMENT_REASON_REQUIRED':
        return 'Ingresa una razón válida para el movimiento.';
      case 'CASH_SESSION_CONTEXT_MISMATCH':
        return 'La caja no pertenece al contexto operativo actual.';
      default:
        return resolveHttpErrorMessage(error, fallback);
    }
  }
}
