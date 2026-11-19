import { DatePipe, CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, DestroyRef, ElementRef, EventEmitter, Injector, Input, OnInit, Output, afterNextRender, effect, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { Subscription } from 'rxjs';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { VAT_CATEGORY_OPTIONS } from '../../../../core/utils/vat-category';
import { Category } from '../../models/category.model';
import { ProductPriceBatchIntentService } from '../../services/product-price-batch-intent.service';
import {
  FiscalBatchCandidate, PriceBatchPreview, FiscalBatchQuery, PriceBatchReceiptSummary, PriceBatchResult, PriceBatchAction, ProductPriceBatchService,
} from '../../services/product-price-batch.service';

const ISSUE_LABELS: Record<string, string> = {
  PRODUCT_NOT_FOUND: 'Producto no disponible en esta empresa',
  PRODUCT_INACTIVE: 'Producto inactivo',
  ZERO_COST: 'Costo cero registrado; revisar rentabilidad',
  ZERO_PRICE: 'Precio propuesto cero',
  BELOW_COST: 'Precio por debajo del costo',
  EXTREME_CHANGE: 'Variacion de al menos 50% de descuento o 100% de aumento',
  PRICE_OUT_OF_RANGE: 'Precio fuera de rango USD',
  PRODUCT_AMOUNT_INVALID: 'Importe actual incompatible',
  PRICE_BATCH_AMOUNT_LIMIT: 'Importe fuera del limite tecnico del lote (USD 1.000.000.000). Valores omitidos para evitar perdida de precision.',
  GROSS_MARGIN_ZERO_COST: 'Costo cero registrado: no permite obtener el margen objetivo',
  GROSS_MARGIN_ROUNDS_TO_ZERO: 'El precio derivado redondea a cero: margen objetivo no calculable',
};

@Component({
  selector: 'app-product-price-batch-dialog',
  imports: [DatePipe, CurrencyPipe, DecimalPipe, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule],
  templateUrl: './product-price-batch-dialog.html',
  styleUrl: './product-price-batch-dialog.scss',
})
export class ProductPriceBatchDialog implements OnInit {
  @Input({ required: true }) categories: Category[] = [];
  @Output() closed = new EventEmitter<void>();
  @Output() applied = new EventEmitter<void>();
  private readonly service = inject(ProductPriceBatchService);
  private readonly intents = inject(ProductPriceBatchIntentService);
  private readonly destroy = inject(DestroyRef);
  private readonly view = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly auth = inject(AuthStore);
  private readonly permissions = inject(PermissionService);
  private readonly initialScope = this.scope();
  private readonly actor = `${this.auth.companyId()}:${this.auth.me()?.userId}`;
  private readonly intentScope = `${this.actor}:${this.auth.establishmentId()}:${this.auth.emissionPointId()}`;
  pending = false;
  recoveryBlocked = false;
  private sequence = 0;
  private dismissed = false;
  private candidateRequest?: Subscription;

  readonly vatCategoryOptions = VAT_CATEGORY_OPTIONS;
  readonly selected = new Set<number>();
  filters: FiscalBatchQuery = { search: '', active: null, categoryId: null, fiscalStatus: null, vatCategory: null };
  candidates: FiscalBatchCandidate[] = [];
  maxProducts = 0;
  total: number | null = null;
  recent: PriceBatchReceiptSummary[] | null = null;
  recentLoading = false;
  recentError = '';
  copyStatus = '';
  action: PriceBatchAction = 'increase-percent';
  value: number | null = 0;
  acknowledged = false;
  readonly actions: { value: PriceBatchAction; label: string }[] = [
    { value: 'increase-percent', label: 'Aumentar %' }, { value: 'decrease-percent', label: 'Disminuir %' },
    { value: 'increase-amount', label: 'Aumentar importe USD' }, { value: 'decrease-amount', label: 'Disminuir importe USD' },
    { value: 'set-price', label: 'Fijar precio USD' }, { value: 'gross-margin', label: 'Margen bruto objetivo %' },
  ];
  reason = '';
  lookupId = '';
  preview: PriceBatchPreview | null = null;
  result: PriceBatchResult | null = null;
  stage: 'selection' | 'declaration' | 'preview' | 'result' = 'selection';
  loading = false;
  busy = false;
  error = '';

  constructor() {
    effect(() => { if (!this.validScope()) this.close(); });
  }

  ngOnInit() {
    try {
      const intent = this.intents.load(this.actor);
      if (intent) {
        this.pending = true;
        if (intent.scope !== this.intentScope) {
          this.recoveryBlocked = true;
          this.error = 'Hay un lote pendiente en otro contexto. Regresa al establecimiento y punto de emision original.';
          return;
        }
        this.preview = intent.preview;
        this.acknowledged = true;
        this.stage = 'preview';
        this.error = 'Resultado pendiente. Consulta el comprobante o reintenta la misma operacion.';
        return;
      }
    } catch {
      this.recoveryBlocked = true;
      this.error = 'No se pudo recuperar el lote pendiente. No se iniciara otra operacion.';
      return;
    }
    this.search();
  }

  filtersChanged() {
    if (this.pending || this.recoveryBlocked) return;
    ++this.sequence;
    this.candidateRequest?.unsubscribe();
    this.loading = false;
    this.candidates = [];
    this.total = null;
    this.error = '';
    this.clearSelection();
  }

  search() {
    if (this.pending || this.recoveryBlocked || this.busy || !this.validScope()) return;
    this.filtersChanged();
    const request = this.sequence;
    this.loading = true;
    this.view.markForCheck();
    this.candidateRequest = this.service.candidates(this.filters).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: data => {
        if (!this.live(request)) return;
        this.maxProducts = data.maxProducts;
        this.candidates = data.totalItems > data.maxProducts ? [] : data.items;
        this.total = data.totalItems;
        this.loading = false;
        this.view.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (!this.live(request)) return;
        this.loading = false;
        this.error = resolveHttpErrorMessage(error, 'No se pudieron consultar los productos.');
        this.view.markForCheck();
      },
    });
  }

  get allSelected() { return this.candidates.length > 0 && this.selected.size === this.candidates.length; }

  toggleAll(checked: boolean) {
    if (this.pending || this.recoveryBlocked || this.busy || this.loading || !this.candidates.length) return;
    if (checked) {
      for (const candidate of this.candidates) this.selected.add(candidate.id);
      this.factsChanged();
    } else this.clearSelection();
  }

  clearSelection() {
    if (this.pending || this.recoveryBlocked) return;
    this.candidateRequest?.unsubscribe();
    ++this.sequence;
    this.loading = false;
    this.selected.clear();
    this.factsChanged();
  }

  toggle(id: number, checked: boolean) {
    if (this.pending || this.recoveryBlocked || this.busy || this.loading || !this.candidates.some(candidate => candidate.id === id)) return;
    if (checked && !this.selected.has(id)) {
      this.selected.add(id);
      this.factsChanged();
    }
    if (!checked && this.selected.delete(id)) this.factsChanged();
  }

  factsChanged() {
    if (this.pending || this.recoveryBlocked) return;
    ++this.sequence;
    this.preview = null;
    this.acknowledged = false;
    this.busy = false;
  }

  editSelection() {
    if (this.pending || this.recoveryBlocked || this.busy) return;
    this.preview = null;
    this.go('selection');
  }

  editDeclaration() {
    if (this.pending || this.recoveryBlocked || this.busy) return;
    if (!this.selected.size) return;
    this.preview = null;
    this.go('declaration');
  }

  exclude(id: number) {
    if (this.pending || this.recoveryBlocked || this.busy) return;
    if (this.selected.delete(id)) this.factsChanged();
    if (this.selected.size) this.editDeclaration();
    else this.editSelection();
  }

  canPreview() { return !this.pending && !this.recoveryBlocked && !this.busy && this.selected.size > 0 && !!this.reason.trim() && this.value !== null && Number.isFinite(this.value) && this.value >= 0 && this.value <= 1000000000
    && Number(this.value.toFixed(4)) === this.value
    && (this.action !== 'gross-margin' || this.value < 100) && (this.action !== 'decrease-percent' || this.value <= 100); }

  review() {
    if (!this.canPreview() || !this.validScope()) return;
    const request = ++this.sequence;
    this.busy = true;
    this.error = '';
    this.service.preview([...this.selected.keys()], this.action, this.value!, this.reason).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: preview => {
        if (!this.live(request)) return;
        this.busy = false;
        this.acknowledged = false;
        this.preview = preview;
        this.go('preview');
      },
      error: (error: HttpErrorResponse) => this.fail(request, error, 'No se pudo generar la previsualización.'),
    });
  }

  confirm() {
    const preview = this.preview;
    if (!this.acknowledged || this.busy || !preview?.previewToken || preview.rejected || !this.validScope()) return;
    if (this.recoveryBlocked) return;
    if (!this.pending) {
      try { this.intents.save(this.actor, this.intentScope, preview); this.pending = true; }
      catch {
        this.recoveryBlocked = true;
        this.error = 'No se pudo preservar la operacion. No se enviaron cambios; reabre para recuperar el estado.';
        return;
      }
    }
    const request = ++this.sequence;
    this.busy = true;
    this.error = '';
    this.service.confirm(preview.operationId, preview.previewToken).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: result => this.complete(request, result),
      error: (error: HttpErrorResponse) => {
        if (!this.live(request)) return;
        if (error.status === 409 && ['PRICE_BATCH_PREVIEW_CHANGED', 'PRICE_BATCH_PREVIEW_EXPIRED',
          'PRICE_BATCH_PREVIEW_INVALID'].includes(error.error?.error)) {
          try { this.intents.clear(this.actor); } catch { this.recoveryBlocked = true; }
          this.pending = false;
          this.preview = null;
          this.go('declaration');
          this.busy = false;
          this.error = 'La previsualización ya no es válida. Revisa los datos y genera otra.';
        } else {
          this.fail(request, error, 'Resultado incierto. Consulta el comprobante o reintenta la misma operacion.');
        }
      },
    });
  }

  recover(id = this.lookupId.trim()) {
    if (this.recoveryBlocked || (this.pending && id !== this.preview?.operationId)) return;
    if (this.busy || this.loading || !this.validScope() || !this.validId(id)) return;
    const request = ++this.sequence;
    this.busy = true;
    this.error = '';
    this.service.receipt(id).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: result => this.complete(request, result),
      error: (error: HttpErrorResponse) => this.fail(request, error, 'No se encontró un comprobante para esta operación.'),
    });
  }

  loadRecent() {
    if (this.recentLoading || this.dismissed || !this.validScope()) return;
    this.recentLoading = true;
    this.recentError = '';
    this.service.recentReceipts().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: receipts => {
        if (this.dismissed || !this.validScope()) return;
        this.recentLoading = false;
        this.recent = receipts;
        this.view.markForCheck();
      },
      error: () => {
        if (this.dismissed || !this.validScope()) return;
        this.recentLoading = false;
        this.recent = null;
        this.recentError = 'No se pudieron consultar los comprobantes recientes.';
        this.view.markForCheck();
      },
    });
  }

  async copyReference(result: PriceBatchResult) {
    this.copyStatus = '';
    let status: string;
    try {
      await navigator.clipboard.writeText(result.operationId);
      status = 'Referencia copiada.';
    } catch {
      status = 'No se pudo copiar. Selecciona la referencia para copiarla manualmente.';
    }
    if (this.dismissed || !this.validScope() || this.result !== result) return;
    this.copyStatus = status;
    this.view.markForCheck();
  }

  validId(value: string) { return /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(value.trim()); }
  close() {
    if (this.dismissed) return;
    this.dismissed = true;
    this.candidateRequest?.unsubscribe();
    this.closed.emit();
  }

  fiscalStatus(status: FiscalBatchCandidate['fiscalStatus']) {
    return { pending: 'Sin declaración', stale: 'Declaración desactualizada', current: 'Declaración vigente' }[status];
  }
  vat(value: number | null) { return VAT_CATEGORY_OPTIONS.find(option => option.value === value)?.label ?? '—'; }
  issue(code: string) { return ISSUE_LABELS[code] ?? code; }
  actionLabel(action: PriceBatchAction) { return this.actions.find(item => item.value === action)?.label ?? action; }

  private complete(request: number, result: PriceBatchResult) {
    if (!this.live(request)) return;
    if (this.pending && result.operationId !== this.preview?.operationId) return;
    try { if (this.pending) this.intents.clear(this.actor); }
    catch {
      this.recoveryBlocked = true;
      this.error = 'Comprobante confirmado; no se pudo liberar el estado local. Reabre para recuperar el mismo lote.';
    }
    this.pending = false;
    this.busy = false;
    this.result = result;
    this.copyStatus = '';
    this.go('result');
    this.applied.emit();
  }
  private fail(request: number, error: HttpErrorResponse, fallback: string) {
    if (!this.live(request)) return;
    this.busy = false;
    this.error = resolveHttpErrorMessage(error, fallback);
    this.view.markForCheck();
  }
  private go(stage: typeof this.stage) {
    this.stage = stage;
    this.error = '';
    this.view.markForCheck();
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[data-batch-focus]')?.focus(), { injector: this.injector });
  }
  private live(request: number) { return request === this.sequence && !this.dismissed && this.validScope(); }
  private scope() { return [this.auth.companyId(), this.auth.establishmentId(), this.auth.emissionPointId(), this.auth.token()].join(':'); }
  private validScope() {
    return this.initialScope === this.scope() && [PERMISSIONS.catalogProductsRead, PERMISSIONS.catalogProductPricesBatch].every(code => this.permissions.hasPermission(code));
  }
}
