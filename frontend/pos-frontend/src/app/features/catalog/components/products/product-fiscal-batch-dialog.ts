import { DatePipe, PercentPipe } from '@angular/common';
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
import { ProductFiscalFields, classificationComplete, emptyProductClassification } from '../../../fiscal-settings/components/product-fiscal-fields';
import { DeclareProductFiscalClassification } from '../../../fiscal-settings/models/fiscal-profile.model';
import { Category } from '../../models/category.model';
import {
  FiscalBatchCandidate, FiscalBatchPreview, FiscalBatchQuery, FiscalBatchReceiptSummary, FiscalBatchResult, ProductFiscalBatchService,
} from '../../services/product-fiscal-batch.service';

const ISSUE_LABELS: Record<string, string> = {
  PRODUCT_NOT_FOUND: 'Producto no disponible en esta empresa',
  PRODUCT_INACTIVE: 'Producto inactivo',
  CATEGORY_INACTIVE: 'Categoría inactiva',
  INSUFFICIENT_STOCK: 'Sin existencias en este establecimiento',
  PRODUCT_FISCAL_CLASSIFICATION_REQUIRED: 'Falta una declaración fiscal vigente',
  NON_INVENTORY_REQUIRES_CURRENT_SERVICE: 'El producto sin inventario requiere una declaración vigente de servicio',
  FISCAL_PROFILE_REQUIRED: 'Falta un perfil fiscal vigente de la empresa',
  FISCAL_PROFILE_CONFIRMATION_REQUIRED: 'El perfil fiscal no está confirmado',
  FISCAL_PROFILE_INCOMPATIBLE: 'Perfil fiscal incompatible',
  RIMPE_EXCLUDED_ACTIVITY_UNSUPPORTED: 'Actividad excluida no compatible con el perfil RIMPE',
  PRODUCT_FISCAL_CLASSIFICATION_INVALID: 'Hechos o referencias incompletos o incompatibles',
  FISCAL_ACTIVITY_UNDETERMINED: 'Actividad fiscal sin determinar',
  FISCAL_CLASSIFICATION_CONFLICT: 'El fundamento no coincide con el IVA nominal',
  FISCAL_LEGAL_BASIS_UNSUPPORTED: 'Combinación de fundamento y hechos no admitida',
  FISCAL_OPERATION_FACTS_UNSUPPORTED: 'Tratamiento fiscal no admitido',
  SRI_MATERIAL_CODE_REQUIRED: 'Falta confirmar el material y su código oficial',
};

@Component({
  selector: 'app-product-fiscal-batch-dialog',
  imports: [DatePipe, PercentPipe, FormsModule, ButtonModule, DialogModule, InputTextModule, MessageModule, ProductFiscalFields],
  templateUrl: './product-fiscal-batch-dialog.html',
  styleUrl: './product-fiscal-batch-dialog.scss',
})
export class ProductFiscalBatchDialog implements OnInit {
  @Input({ required: true }) categories: Category[] = [];
  @Output() closed = new EventEmitter<void>();
  @Output() applied = new EventEmitter<void>();
  private readonly service = inject(ProductFiscalBatchService);
  private readonly destroy = inject(DestroyRef);
  private readonly view = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly auth = inject(AuthStore);
  private readonly permissions = inject(PermissionService);
  private readonly initialScope = this.scope();
  private sequence = 0;
  private dismissed = false;
  private candidateRequest?: Subscription;

  readonly vatCategoryOptions = VAT_CATEGORY_OPTIONS;
  readonly selected = new Set<number>();
  filters: FiscalBatchQuery = { search: '', active: null, categoryId: null, fiscalStatus: null, vatCategory: null };
  candidates: FiscalBatchCandidate[] = [];
  maxProducts = 0;
  total: number | null = null;
  recent: FiscalBatchReceiptSummary[] | null = null;
  recentLoading = false;
  recentError = '';
  copyStatus = '';
  draft = emptyProductClassification();
  reason = '';
  lookupId = '';
  preview: FiscalBatchPreview | null = null;
  result: FiscalBatchResult | null = null;
  stage: 'selection' | 'declaration' | 'preview' | 'result' = 'selection';
  loading = false;
  busy = false;
  error = '';

  constructor() {
    effect(() => { if (!this.validScope()) this.close(); });
  }

  ngOnInit() { this.search(); }

  filtersChanged() {
    ++this.sequence;
    this.candidateRequest?.unsubscribe();
    this.loading = false;
    this.candidates = [];
    this.total = null;
    this.error = '';
    this.clearSelection();
  }

  search() {
    if (this.busy || !this.validScope()) return;
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
    if (this.busy || this.loading || !this.candidates.length) return;
    if (checked) {
      for (const candidate of this.candidates) this.selected.add(candidate.id);
      this.factsChanged();
    } else this.clearSelection();
  }

  clearSelection() {
    if (!this.selected.size) return;
    this.selected.clear();
    this.factsChanged();
  }

  toggle(id: number, checked: boolean) {
    if (this.busy || this.loading || !this.candidates.some(candidate => candidate.id === id)) return;
    if (checked && !this.selected.has(id)) {
      this.selected.add(id);
      this.factsChanged();
    }
    if (!checked && this.selected.delete(id)) this.factsChanged();
  }

  factsChanged() {
    this.draft.confirmed = false;
    if (this.draft.taxBasis !== 1) {
      this.draft.naturalAgriculturalFoodConfirmed = false;
      this.draft.foodProcessing = null;
    }
    if (this.draft.taxBasis !== 3) {
      this.draft.constructionMaterialConfirmed = false;
      this.draft.sriMaterialCode = null;
    }
  }

  editSelection() {
    this.preview = null;
    this.go('selection');
  }

  editDeclaration() {
    if (!this.selected.size) return;
    this.preview = null;
    this.go('declaration');
  }

  exclude(id: number) {
    if (this.selected.delete(id)) this.factsChanged();
    if (this.selected.size) this.editDeclaration();
    else this.editSelection();
  }

  canPreview() { return !this.busy && this.selected.size > 0 && !!this.reason.trim() && classificationComplete(this.draft); }

  review() {
    if (!this.canPreview() || !this.validScope()) return;
    const request = ++this.sequence;
    this.busy = true;
    this.error = '';
    this.service.preview([...this.selected.keys()], { ...this.draft }, this.reason).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: preview => {
        if (!this.live(request)) return;
        this.busy = false;
        this.preview = preview;
        this.go('preview');
      },
      error: (error: HttpErrorResponse) => this.fail(request, error, 'No se pudo generar la previsualización.'),
    });
  }

  confirm() {
    const preview = this.preview;
    if (this.busy || !preview?.previewToken || preview.rejected || !this.validScope()) return;
    const request = ++this.sequence;
    this.busy = true;
    this.error = '';
    this.service.confirm(preview.operationId, preview.previewToken).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: result => this.complete(request, result),
      error: (error: HttpErrorResponse) => {
        if (!this.live(request)) return;
        if (error.status === 409) {
          this.preview = null;
          this.go('declaration');
          this.busy = false;
          this.error = 'La previsualización ya no es válida. Revisa los datos y genera otra.';
        } else {
          this.fail(request, error, 'No se pudo confirmar el resultado. Reintenta con la misma previsualización.');
        }
      },
    });
  }

  recover(id = this.lookupId.trim()) {
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

  async copyReference(result: FiscalBatchResult) {
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
  classification(value: Pick<DeclareProductFiscalClassification, 'kind' | 'activity' | 'taxBasis' | 'sriMaterialCode'>) {
    const kind = value.kind === 0 ? 'Bien' : value.kind === 1 ? 'Servicio' : null;
    const activity = ['Comercio RIMPE', 'Servicio RIMPE', 'Actividad excluida'][value.activity ?? -1];
    const basis = ['Tarifa ordinaria', 'Alimento agrícola natural', 'Servicio a tarifa 0%', 'Material listado', 'No objeto de IVA', 'Exención'][value.taxBasis ?? -1];
    return [kind, activity, basis, value.sriMaterialCode].filter(Boolean).join(' · ');
  }

  private complete(request: number, result: FiscalBatchResult) {
    if (!this.live(request)) return;
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
    return this.initialScope === this.scope() && [PERMISSIONS.catalogProductsRead, PERMISSIONS.catalogProductsWrite,
      PERMISSIONS.fiscalSettingsRead, PERMISSIONS.fiscalSettingsWrite].every(code => this.permissions.hasPermission(code));
  }
}
