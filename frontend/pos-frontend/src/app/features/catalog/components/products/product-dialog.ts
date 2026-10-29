import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, EventEmitter, Input, OnChanges, Output, SimpleChanges, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SelectModule } from 'primeng/select';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TabsModule } from 'primeng/tabs';
import { AuthStore } from '../../../../core/stores/auth.store';
import { PermissionService } from '../../../../core/services/permission.service';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import { DEFAULT_VAT_CATEGORY, VAT_CATEGORY_OPTIONS, normalizeVatCategory } from '../../../../core/utils/vat-category';
import { ProductFiscalFields, classificationComplete, emptyProductClassification } from '../../../fiscal-settings/components/product-fiscal-fields';
import { Category } from '../../models/category.model';
import { Product, ProductEditorState } from '../../models/product.model';
import { CategoryService } from '../../services/category.service';
import { ProductService } from '../../services/product.service';

type EditableField = 'name' | 'categoryId' | 'barcode' | 'internalCode' | 'price' | 'cost' | 'minimumStock' | 'vatCategory' | 'tracksInventory';
interface ReconciliationRow { field: EditableField; label: string; draft: string; current: string; choice: 'draft' | 'current'; }

@Component({
  selector: 'app-product-dialog', standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, SelectModule,
    InputNumberModule, ButtonModule, MessageModule, TabsModule, ProductFiscalFields],
  templateUrl: './product-dialog.html', styleUrl: './product-dialog.scss',
})
export class ProductDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly categoryService = inject(CategoryService);
  private readonly service = inject(ProductService);
  private readonly auth = inject(AuthStore);
  private readonly permission = inject(PermissionService);
  private readonly destroyRef = inject(DestroyRef);
  private generation = 0;
  private loadedScope = '';
  private activeId: number | null = null;
  private state: ProductEditorState | null = null;
  @Input({ required: true }) visible = false;
  @Input() product: Product | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() saved = new EventEmitter<ProductEditorState>();
  readonly categories = signal<Category[]>([]);
  readonly vatCategoryOptions = VAT_CATEGORY_OPTIONS;
  section = 'basic';
  loading = false;
  saving = false;
  error = '';
  success = '';
  reviewRequired = false;
  reviewed = false;
  conflict = false;
  uncertain = false;
  scopeChanged = false;
  draft = emptyProductClassification();
  pending: string[] = [];
  comparison: ProductEditorState | null = null;
  comparisonLoading = false;
  reconciliation: ReconciliationRow[] = [];
  candidates: Product[] = [];
  fiscalChoice: 'draft' | 'current' = 'current';
  readonly kinds = [{ label: 'Bien', value: 0 }, { label: 'Servicio', value: 1 }];
  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]], tracksInventory: [true],
    kind: this.fb.control<number | null>(null), categoryId: [0, [Validators.required, Validators.min(1)]],
    barcode: ['', Validators.maxLength(100)], internalCode: ['', Validators.maxLength(100)],
    price: [0, [Validators.required, Validators.min(0)]], cost: [0, [Validators.required, Validators.min(0)]],
    minimumStock: [3, [Validators.required, Validators.min(0)]], vatCategory: [DEFAULT_VAT_CATEGORY, Validators.required],
  });

  constructor() {
    this.form.controls.kind.valueChanges.pipe(takeUntilDestroyed()).subscribe(kind => {
      this.draft.kind = kind;
      this.draft.confirmed = false;
      if (kind === 1) this.form.controls.tracksInventory.setValue(false);
      else if (kind === 0) this.form.controls.tracksInventory.setValue(true);
      this.syncInventoryControl();
    });
    this.form.controls.tracksInventory.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.syncInventoryControl());
    // Invalidate all callbacks on session/context changes, including a reopen of the same ID.
    effect(() => {
      const scope = this.scope();
      if (this.visible && this.loadedScope && this.loadedScope !== scope) {
        this.generation++;
        this.scopeChanged = true;
        this.saving = false; this.loading = false;
        this.error = 'La sesion o el contexto cambio. Cierra y vuelve a abrir el registro en el contexto correcto.';
      }
    });
  }

  get isEditMode() { return this.activeId !== null; }
  canClassify() { return this.permission.hasPermission(PERMISSIONS.fiscalSettingsWrite)
    && this.permission.hasPermission(PERMISSIONS.fiscalSettingsRead); }
  canSave() { return !this.loading && !this.saving && !this.uncertain && !this.conflict && !this.scopeChanged
    && !this.comparisonLoading && !this.comparison
    && this.visible && this.loadedScope === this.scope()
    && this.permission.hasPermission(PERMISSIONS.catalogProductsWrite)
    && (!this.reviewRequired || this.reviewed); }
  canDisableInventory() { return this.state?.canDisableInventory || (this.canClassify() && this.form.controls.kind.value === 1); }
  classificationValid() { return classificationComplete(this.classificationPayload()); }
  private scope() { return [this.auth.companyId(), this.auth.establishmentId(), this.auth.emissionPointId(), this.auth.token()].join(':'); }
  private current(generation: number, scope: string) { return this.visible && generation === this.generation && scope === this.scope(); }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['visible'] && !changes['product']) return;
    const generation = ++this.generation;
    if (!this.visible) return;
    const scope = this.scope(); this.loadedScope = scope;
    this.activeId = this.product?.id ?? null;
    this.state = null; this.error = ''; this.success = ''; this.section = 'basic';
    this.saving = false; this.loading = false; this.conflict = false; this.uncertain = false;
    this.scopeChanged = false; this.reviewRequired = false; this.reviewed = false;
    this.draft = emptyProductClassification(); this.pending = [];
    this.comparison = null; this.comparisonLoading = false; this.reconciliation = []; this.candidates = [];
    this.form.reset({ name: '', tracksInventory: true, kind: null, categoryId: 0, barcode: '', internalCode: '',
      price: 0, cost: 0, minimumStock: 3, vatCategory: DEFAULT_VAT_CATEGORY });
    if (this.canClassify()) this.form.controls.kind.enable({ emitEvent: false });
    else this.form.controls.kind.disable({ emitEvent: false });
    this.syncInventoryControl();
    if (this.activeId !== null) {
      this.loading = true;
      this.service.editor(this.activeId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: state => {
          if (!this.current(generation, scope)) return;
          this.loading = false;
          this.reviewRequired = !!this.product && !this.sameProduct(this.product, state.product);
          this.acceptState(state);
        },
        error: (error: HttpErrorResponse) => {
          if (!this.current(generation, scope)) return;
          this.loading = false; this.conflict = true;
          this.error = resolveHttpErrorMessage(error, 'No se pudo cargar el estado actual.');
        },
      });
    }
    this.categoryService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: categories => { if (this.current(generation, scope)) this.categories.set(categories); },
      error: () => { if (this.current(generation, scope)) this.categories.set([]); },
    });
  }

  availableCategories() {
    return this.categories().filter(c => c.isActive || (this.state?.product.isActive === false && c.id === this.state.product.categoryId));
  }

  hide(): void {
    if (this.saving) return;
    this.generation++;
    this.visibleChange.emit(false);
  }

  save(complete = false): void {
    if (!this.canSave()) return;
    if (this.form.invalid || (complete && (!this.canClassify() || !this.classificationValid()))) {
      this.form.markAllAsTouched();
      this.error = complete ? 'Completa y confirma la declaracion fiscal con referencias del producto.' : 'Revisa los datos basicos obligatorios.';
      return;
    }
    if (!this.form.controls.tracksInventory.value && !complete && !this.state?.canDisableInventory
      && this.state?.product.tracksInventory !== false) {
      this.error = 'Sin inventario requiere una declaracion vigente de Servicio. Completa Fiscal y usa Guardar completo.';
      this.section = 'fiscal'; return;
    }
    const generation = this.generation; const scope = this.scope();
    const values = this.form.getRawValue();
    const payload = { categoryId: values.categoryId, name: values.name.trim(), price: values.price,
      cost: values.cost, minimumStock: values.minimumStock, vatCategory: values.vatCategory,
      setManualCost: this.form.controls.cost.dirty,
      tracksInventory: values.tracksInventory, barcode: values.barcode.trim() || null,
      internalCode: values.internalCode.trim() || null, expectedStateToken: this.state?.stateToken ?? null,
      classification: complete ? this.classificationPayload() : null };
    this.saving = true; this.error = ''; this.success = '';
    this.service.saveEditor(this.activeId, payload).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: state => {
        if (!this.current(generation, scope)) return;
        const draft = this.draft; const kind = this.form.controls.kind.value;
        this.saving = false; this.acceptState(state); this.reviewRequired = false;
        if (!complete) {
          this.draft = { ...draft, confirmed: false };
          this.form.controls.kind.setValue(kind, { emitEvent: false });
          this.syncInventoryControl();
        }
        this.success = 'Guardado. Registro #' + state.product.id;
        this.saved.emit(state);
      },
      error: (error: HttpErrorResponse) => {
        if (!this.current(generation, scope)) return;
        this.saving = false;
        this.uncertain = error.status === 0 || error.status >= 500;
        this.conflict = error.error?.error === 'PRODUCT_EDITOR_STATE_CONFLICT'
          || error.error?.error === 'PRODUCT_FISCAL_REVISION_CONFLICT';
        this.error = this.uncertain
          ? 'Resultado incierto: no repitas el alta. Conservamos los valores; revisa el catalogo por codigo/nombre antes de continuar.'
          : this.conflict ? 'El registro cambio. Conservamos tu borrador; revisa el estado actual antes de aplicar cambios.'
          : resolveHttpErrorMessage(error, 'No se pudo guardar. Los valores se conservan.');
      },
    });
  }

  reviewCurrent(id = this.activeId): void {
    if (id === null || this.comparisonLoading || this.saving || this.scopeChanged || this.loadedScope !== this.scope()) return;
    const generation = this.generation; const scope = this.scope();
    this.comparisonLoading = true;
    this.service.editor(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: state => {
        if (!this.current(generation, scope)) return;
        this.comparisonLoading = false; this.comparison = state;
        const base = this.state ? this.productValues(this.state.product) : null;
        const draft = this.form.getRawValue(); const latest = this.productValues(state.product);
        const fields: [EditableField, string][] = [['name', 'Nombre'], ['categoryId', 'Categoria'], ['barcode', 'Codigo de barras'],
          ['internalCode', 'Codigo interno'], ['price', 'Precio'], ['cost', 'Costo'], ['minimumStock', 'Stock minimo'],
          ['vatCategory', 'IVA nominal'], ['tracksInventory', 'Inventario']];
        this.reconciliation = fields.map(([field, label]) => ({ field, label,
          draft: this.displayValue(draft[field]), current: this.displayValue(latest[field]),
          choice: base && base[field] === latest[field] ? 'draft' : 'current' }));
        this.fiscalChoice = this.state?.classificationRevision === state.classificationRevision ? 'draft' : 'current';
      },
      error: (error: HttpErrorResponse) => {
        if (!this.current(generation, scope)) return;
        this.comparisonLoading = false; this.error = resolveHttpErrorMessage(error, 'No se pudo consultar el estado actual. Tu borrador se conserva.');
      },
    });
  }

  reconcile(): void {
    const latest = this.comparison;
    if (!latest || this.loadedScope !== this.scope() || this.scopeChanged || this.comparisonLoading) return;
    const draft = this.form.getRawValue();
    const manualCost = this.form.controls.cost.dirty;
    const values = this.productValues(latest.product);
    const merged = { ...values };
    // Remote changes win by default. Overwriting one requires a per-field human choice.
    for (const row of this.reconciliation) {
      if (row.choice === 'draft') Object.assign(merged, { [row.field]: draft[row.field] });
    }
    const fiscal = this.draft;
    const keepFiscal = this.fiscalChoice === 'draft';
    this.acceptState(latest);
    if (keepFiscal) this.draft = { ...fiscal, confirmed: false };
    this.form.setValue({ ...merged, kind: keepFiscal ? draft.kind : latest.classification?.kind ?? null }, { emitEvent: false });
    if (manualCost && this.reconciliation.find(row => row.field === 'cost')?.choice === 'draft')
      this.form.controls.cost.markAsDirty();
    this.syncInventoryControl(); this.form.markAsDirty();
    this.comparison = null; this.candidates = []; this.conflict = false; this.uncertain = false;
    this.reviewRequired = false; this.reviewed = true;
    this.error = ''; this.success = 'Borrador reconciliado. La declaracion fiscal requiere nueva confirmacion.';
  }

  findUncertainCreation(): void {
    if (!this.uncertain || this.activeId !== null || this.comparisonLoading || this.scopeChanged || this.loadedScope !== this.scope()) return;
    const generation = this.generation; const scope = this.scope(); const values = this.form.getRawValue();
    this.comparisonLoading = true;
    this.service.lookup(values.internalCode.trim() || values.barcode.trim() || values.name.trim(), 30)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: products => {
          if (!this.current(generation, scope)) return;
          this.comparisonLoading = false; this.candidates = products;
          if (!products.length) this.error = 'Sin coincidencias en esta consulta. El resultado sigue incierto; no se repetira el alta.';
        },
        error: (error: HttpErrorResponse) => {
          if (!this.current(generation, scope)) return;
          this.comparisonLoading = false; this.error = resolveHttpErrorMessage(error, 'No se pudo reconciliar el catalogo. Tu borrador se conserva.');
        },
      });
  }

  private productValues(p: Product) {
    return { name: p.name, tracksInventory: p.tracksInventory !== false, categoryId: p.categoryId,
      barcode: p.barcode ?? '', internalCode: p.internalCode ?? '', price: p.price, cost: p.cost,
      minimumStock: p.minimumStock, vatCategory: normalizeVatCategory(p.vatCategory) };
  }
  private displayValue(value: string | number | boolean) { return typeof value === 'boolean' ? (value ? 'Si' : 'No') : String(value || (value === 0 ? 0 : 'Sin dato')); }

  private classificationPayload() {
    return { ...this.draft, kind: this.form.controls.kind.value,
      expectedRevision: this.state?.classificationRevision ?? 0, expectedProductRevision: this.state?.productRevision ?? 0,
      sriMaterialCode: this.draft.taxBasis === 3 ? this.draft.sriMaterialCode : null };
  }

  private acceptState(state: ProductEditorState) {
    this.state = state; this.activeId = state.product.id; this.pending = state.salePending;
    const p = state.product;
    this.form.setValue({ name: p.name, tracksInventory: p.tracksInventory !== false, kind: state.classification?.kind ?? null,
      categoryId: p.categoryId, barcode: p.barcode ?? '', internalCode: p.internalCode ?? '', price: p.price,
      cost: p.cost, minimumStock: p.minimumStock, vatCategory: normalizeVatCategory(p.vatCategory) }, { emitEvent: false });
    // A historic declaration is never a new human confirmation.
    this.draft = state.classification ? { ...state.classification, expectedRevision: state.classificationRevision,
      expectedProductRevision: state.productRevision, confirmed: false } : emptyProductClassification();
    this.form.markAsPristine();
    this.syncInventoryControl();
  }

  private syncInventoryControl() {
    const control = this.form.controls.tracksInventory;
    if (control.value && !this.canDisableInventory()) control.disable({ emitEvent: false });
    else control.enable({ emitEvent: false });
  }

  pendingLabel(code: string) {
    return resolveHttpErrorMessage(new HttpErrorResponse({ status: 409, error: { error: code } }));
  }

  private sameProduct(a: Product, b: Product) {
    return a.name === b.name && a.categoryId === b.categoryId && a.price === b.price && a.cost === b.cost
      && a.minimumStock === b.minimumStock && a.vatCategory === b.vatCategory && a.isActive === b.isActive
      && (a.barcode ?? null) === (b.barcode ?? null) && (a.internalCode ?? null) === (b.internalCode ?? null)
      && (a.tracksInventory !== false) === (b.tracksInventory !== false);
  }
}
