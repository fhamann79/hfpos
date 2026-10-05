import { CommonModule, CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
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
import { OperationIntent, definitiveOperationRejection, operationActor, operationScope } from '../../../../core/utils/operation-intent';
import { of, switchMap } from 'rxjs';
import {
  CashMovement,
  CashMovementType,
  CashSession,
  CashSessionListItem,
  CashSessionStatus,
  CashSessionSummary,
  CreateCashMovementRequest,
  OpenCashSessionRequest,
  CloseCashSessionRequest,
} from '../../models/cash-session.model';
import { CashSessionService } from '../../services/cash-session.service';
import { PaymentReconciliationPanel } from '../../components/payment-reconciliation-panel/payment-reconciliation-panel';

interface SelectOption<T> {
  label: string;
  value: T;
}

const EMPTY_SUMMARY: CashSessionSummary = {
  openCount: 0,
  closedCount: 0,
};

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
    PaymentReconciliationPanel,
  ],
  providers: [MessageService],
  templateUrl: './cash-sessions-page.html',
  styleUrl: './cash-sessions-page.scss',
})
export class CashSessionsPage implements OnInit, OnDestroy {
  private readonly cashSessionService = inject(CashSessionService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);
  private readonly messageService = inject(MessageService);
  private currentSequence = 0;
  private destroyed = false;
  private movementTarget: { id: number; scope: string } | null = null;
  private closeTarget: { id: number; scope: string } | null = null;
  private readonly movementIntent = new OperationIntent<{ id: number; payload: CreateCashMovementRequest }>('cash-movement', operationActor(this.authStore));
  private readonly closeIntent = new OperationIntent<{ id: number; payload: CloseCashSessionRequest }>('cash-close', operationActor(this.authStore));
  private readonly openIntent = new OperationIntent<OpenCashSessionRequest>('cash-open', operationActor(this.authStore));
  readonly movementLocked = signal(this.movementIntent.pending);
  readonly closeLocked = signal(this.closeIntent.pending);
  readonly openLocked = signal(this.openIntent.pending);
  readonly closingSession = signal<CashSession | null>(null);
  get movementTargetId(): number | null { return this.movementTarget?.id ?? null; }
  get closeTargetId(): number | null { return this.closeTarget?.id ?? null; }

  ngOnDestroy(): void { this.destroyed = true; ++this.currentSequence; }

  readonly currentSession = signal<CashSession | null>(null);
  readonly sessions = signal<CashSessionListItem[]>([]);
  readonly summary = signal<CashSessionSummary>(EMPTY_SUMMARY);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly currentPage = signal(1);
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

  readonly totalOpenSessions = computed(() => this.summary().openCount);
  readonly totalClosedSessions = computed(() => this.summary().closedCount);

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
  first = 0;
  rows = 15;

  ngOnInit(): void {
    this.refreshAll();
  }

  refreshAll(): void {
    this.loadCurrent();
    this.loadSessions();
  }

  loadCurrent(): void {
    const sequence = ++this.currentSequence;
    const scope = operationScope(this.authStore);
    this.currentLoading.set(true);

    this.cashSessionService.getCurrent().subscribe({
      next: (session) => {
        if (this.destroyed || sequence !== this.currentSequence || scope !== operationScope(this.authStore)) return;
        this.currentSession.set(session);
        this.currentLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || sequence !== this.currentSequence || scope !== operationScope(this.authStore)) return;
        this.currentLoading.set(false);
        this.messageService.add({
          severity: 'error',
          summary: 'Caja',
          detail: this.resolveCashError(error, 'No se pudo consultar la caja actual.'),
        });
      },
    });
  }

  loadSessions(page = this.currentPage(), pageSize = this.rows): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.cashSessionService
      .getAll({
        from: this.from,
        to: this.to,
        status: this.status,
        userId: this.userId,
        page,
        pageSize,
      })
      .subscribe({
        next: (result) => {
          if (result.totalPages > 0 && result.page > result.totalPages) {
            this.loadSessions(result.totalPages, result.pageSize);
            return;
          }

          this.sessions.set(result.items);
          this.summary.set(result.summary ?? EMPTY_SUMMARY);
          this.totalItems.set(result.totalItems);
          this.totalPages.set(result.totalPages);
          this.rows = result.pageSize;
          this.first = result.totalItems === 0 ? 0 : (result.page - 1) * result.pageSize;
          this.currentPage.set(result.totalItems === 0 ? 1 : result.page);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          this.errorMessage.set(this.resolveCashError(error, 'No se pudo cargar el historial de caja.'));
        },
      });
  }

  onSessionsLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    const first = event.first ?? this.first;
    this.loadSessions(Math.floor(first / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.currentPage.set(1);
    this.loadSessions(1, this.rows);
  }

  clearFilters(): void {
    this.from = '';
    this.to = '';
    this.status = null;
    this.userId = null;
    this.applyFilters();
  }

  openCashDialog(): void {
    if (this.saving()) return;
    if (!this.canWrite()) {
      return;
    }

    this.openingAmount = 0;
    this.openingNotes = '';
    this.formError.set('');
    this.openDialogVisible = true;
    if (this.openIntent.pending) {
      try {
        const pending = this.openIntent.retry(operationScope(this.authStore));
        this.openingAmount = pending.openingAmount;
        this.openingNotes = pending.openingNotes ?? '';
      } catch (error) { this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.'); }
    }
  }

  closeOpenDialog(): void {
    if (this.saving()) { this.openDialogVisible = true; return; }
    this.openDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmOpen(): void {
    if (!this.canWrite() || this.saving() || this.destroyed) {
      return;
    }

    const wasRetry = this.openIntent.pending;
    const amount = this.parseAmount(this.openingAmount);
    if (amount === null || amount < 0) {
      this.formError.set('El monto inicial debe ser mayor o igual a 0.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');
    const scope = operationScope(this.authStore);
    let payload: OpenCashSessionRequest;
    try {
      payload = this.openIntent.capture(scope, { requestId: crypto.randomUUID(), openingAmount: amount, openingNotes: this.normalizeOptionalText(this.openingNotes) });
    } catch (error) {
      this.saving.set(false);
      this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.');
      return;
    }
    this.openLocked.set(true);
    ++this.currentSequence;
    const request = this.cashSessionService.open(payload);
    request.subscribe({
      next: (session) => {
        if (this.destroyed || scope !== operationScope(this.authStore)) return;
        this.saving.set(false);
        if (!session) {
          this.formError.set('Respuesta incompleta. Conserva la apertura pendiente para recuperar su resultado.');
          return;
        }
        ++this.currentSequence;
        this.currentLoading.set(false);
        this.openIntent.clear();
        this.openLocked.set(false);
        this.openDialogVisible = false;
        this.currentSession.set(session.status === CashSessionStatus.Open ? session : null);
        this.selectedSession.set(session);
        if (session.status === CashSessionStatus.Closed) this.detailDialogVisible = true;
        this.messageService.add({ severity: 'success', summary: wasRetry ? 'Estado de caja recuperado' : 'Caja abierta', detail: 'Consulta los datos de la caja actual.' });
        this.loadSessions();
        this.loadCurrent();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || scope !== operationScope(this.authStore)) return;
        this.saving.set(false);
        if (!wasRetry && definitiveOperationRejection(error)) {
          this.openIntent.clear(); this.openLocked.set(false);
        }
        this.formError.set(this.resolveCashError(error, 'No se pudo abrir la caja.'));
      },
    });
  }

  openMovementDialog(type: CashMovementType): void {
    if (!this.canWrite() || this.saving() || (!this.currentSession() && !this.movementIntent.pending)) {
      return;
    }

    this.movementType = type;
    this.movementAmount = null;
    this.movementReason = '';
    this.formError.set('');
    this.movementDialogVisible = true;
    const scope = operationScope(this.authStore);
    this.movementTarget = this.currentSession() ? { id: this.currentSession()!.id, scope } : null;
    if (this.movementIntent.pending) {
      try {
        const pending = this.movementIntent.retry(scope);
        this.movementTarget = { id: pending.id, scope };
        this.movementType = pending.payload.type;
        this.movementAmount = pending.payload.amount;
        this.movementReason = pending.payload.reason;
      } catch (error) { this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.'); }
    }
  }

  closeMovementDialog(): void {
    if (this.saving()) { this.movementDialogVisible = true; return; }
    this.movementDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmMovement(): void {
    const target = this.movementTarget;
    if (!this.canWrite() || !target || this.saving() || this.destroyed
      || target.scope !== operationScope(this.authStore)) {
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
    const wasRetry = this.movementIntent.pending;
    let frozen: { id: number; payload: CreateCashMovementRequest };
    try {
      frozen = this.movementIntent.capture(target.scope, { id: target.id,
        payload: { requestId: crypto.randomUUID(), type: this.movementType, amount, reason } });
    } catch (error) {
      this.saving.set(false); this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.'); return;
    }
    this.movementLocked.set(true);
    ++this.currentSequence;
    this.cashSessionService.addMovement(frozen.id, frozen.payload).subscribe({
      next: (updated) => {
        if (this.destroyed || target.scope !== operationScope(this.authStore)) return;
        ++this.currentSequence;
        this.currentLoading.set(false);
        this.movementIntent.clear(); this.movementLocked.set(false);
        this.saving.set(false);
        this.movementDialogVisible = false;
        this.currentSession.set(updated.status === CashSessionStatus.Open ? updated : null);
        this.selectedSession.set(updated);
        this.messageService.add({ severity: 'success', summary: 'Movimiento registrado', detail: 'La caja fue actualizada.' });
        this.loadSessions();
        this.loadCurrent();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || target.scope !== operationScope(this.authStore)) return;
        this.saving.set(false);
        if (!wasRetry && definitiveOperationRejection(error)) {
          this.movementIntent.clear(); this.movementLocked.set(false);
        }
        this.formError.set(this.resolveCashError(error, 'No se pudo registrar el movimiento.'));
      },
    });
  }

  openCloseDialog(): void {
    const session = this.currentSession();
    if (!this.canWrite() || this.saving() || (!session && !this.closeIntent.pending)) {
      return;
    }

    const scope = operationScope(this.authStore);
    this.closeTarget = session ? { id: session.id, scope } : null;
    this.closingSession.set(session);
    this.countedCashAmount = session?.expectedCashAmount ?? 0;
    this.closingNotes = '';
    this.formError.set('');
    this.closeDialogVisible = true;
    if (this.closeIntent.pending) {
      try {
        const pending = this.closeIntent.retry(scope);
        this.closeTarget = { id: pending.id, scope };
        this.countedCashAmount = pending.payload.countedCashAmount;
        this.closingNotes = pending.payload.closingNotes ?? '';
      } catch (error) { this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.'); }
    }
  }

  closeCloseDialog(): void {
    if (this.saving()) { this.closeDialogVisible = true; return; }
    this.closeDialogVisible = false;
    this.formError.set('');
    this.saving.set(false);
  }

  confirmClose(): void {
    const target = this.closeTarget;
    if (!this.canWrite() || !target || this.saving() || this.destroyed
      || target.scope !== operationScope(this.authStore)) {
      return;
    }

    const countedAmount = this.parseAmount(this.countedCashAmount);
    if (countedAmount === null || countedAmount < 0) {
      this.formError.set('El efectivo contado debe ser mayor o igual a 0.');
      return;
    }

    this.saving.set(true);
    this.formError.set('');
    const wasRetry = this.closeIntent.pending;
    let frozen: { id: number; payload: CloseCashSessionRequest };
    try {
      frozen = this.closeIntent.capture(target.scope, { id: target.id,
        payload: { countedCashAmount: countedAmount, closingNotes: this.normalizeOptionalText(this.closingNotes) } });
    } catch (error) {
      this.saving.set(false); this.formError.set(error instanceof Error ? error.message : 'Operacion pendiente.'); return;
    }
    this.closeLocked.set(true);
    ++this.currentSequence;
    const request = wasRetry ? this.cashSessionService.getById(frozen.id).pipe(switchMap(value =>
      value.status === CashSessionStatus.Closed ? of(value) : this.cashSessionService.close(frozen.id, frozen.payload)))
      : this.cashSessionService.close(frozen.id, frozen.payload);
    request.subscribe({
      next: (closed) => {
        if (this.destroyed || target.scope !== operationScope(this.authStore)) return;
        ++this.currentSequence;
        this.currentLoading.set(false);
        this.closeIntent.clear(); this.closeLocked.set(false);
        this.saving.set(false);
        this.closeDialogVisible = false;
        this.currentSession.set(null);
        this.selectedSession.set(closed);
        this.detailDialogVisible = true;
        this.messageService.add({ severity: 'success', summary: wasRetry ? 'Estado de cierre recuperado' : 'Caja cerrada', detail: 'Consulta los valores de cierre registrados.' });
        this.loadSessions();
        this.loadCurrent();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || target.scope !== operationScope(this.authStore)) return;
        this.saving.set(false);
        if (!wasRetry && definitiveOperationRejection(error)) {
          this.closeIntent.clear(); this.closeLocked.set(false);
        }
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
