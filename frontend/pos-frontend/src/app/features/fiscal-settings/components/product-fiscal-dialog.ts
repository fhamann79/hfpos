import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, EventEmitter, Input, OnChanges, Output, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { AuthStore } from '../../../core/stores/auth.store';
import { PermissionService } from '../../../core/services/permission.service';
import { PERMISSIONS } from '../../../core/constants/permissions';
import { resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { DeclareProductFiscalClassification, ProductFiscalState } from '../models/fiscal-profile.model';
import { FiscalProfileService } from '../services/fiscal-profile.service';
import { ProductFiscalFields, classificationComplete } from './product-fiscal-fields';

@Component({
  selector: 'app-product-fiscal-dialog', standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, ProductFiscalFields, MessageModule],
  styleUrl: './fiscal-profile-panel.scss',
  template: `
    <p-dialog header="Clasificación fiscal del producto" [visible]="visible" (visibleChange)="close($event)"
      [modal]="true" [draggable]="false" [style]="{width:'46rem',maxWidth:'95vw'}" [closable]="!saving">
      <section class="profile-panel">
        <strong>{{ identity?.name ?? product?.name }}</strong>
        @if (identity) { <small>{{ identity.internalCode ?? 'Sin código interno' }} · {{ identity.barcode ?? 'Sin código de barras' }}</small> }
        @if (identity && (identity.name !== product?.name || (product?.vatCategory !== undefined && identity.vatCategory !== product?.vatCategory))) {
          <p-message severity="warn" text="El producto cambió desde que cargaste el catálogo. Revisa los datos actuales mostrados antes de confirmar." />
        }
        @if (identity?.vatCategory === 3 || identity?.vatCategory === 4) { <p-message severity="warn" text="Exento y no objeto requieren hechos específicos por operación todavía no implementados. La emisión de este producto permanece bloqueada." /> }
        @if (error) { <p-message severity="error" [text]="error" /> }
        @if (current === false) { <p-message severity="warn" text="Clasificación ausente o desactualizada. Confirma los hechos del producto antes de emitir." /> }
        <form #form="ngForm" (ngSubmit)="save()"><fieldset [disabled]="loading || saving">
          <app-product-fiscal-fields [draft]="draft" />
          <footer><p-button type="button" label="Cancelar" severity="secondary" [disabled]="saving" (onClick)="close(false)" /><p-button type="submit" label="Confirmar clasificación" icon="pi pi-check" [loading]="saving" [disabled]="!complete() || loading || !canSave()" /></footer>
        </fieldset></form>
      </section>
    </p-dialog>`,
})
export class ProductFiscalDialog implements OnChanges {
  @Input() visible = false;
  @Input() product: { id: number; name: string; vatCategory?: number } | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  private readonly service = inject(FiscalProfileService);
  private readonly auth = inject(AuthStore);
  private readonly permission = inject(PermissionService);
  private readonly destroyRef = inject(DestroyRef);
  private generation = 0;
  private loadedScope = '';
  loading = false; saving = false; error = ''; current: boolean | null = null;
  identity: ProductFiscalState | null = null;
  draft = this.empty();
  complete() { return classificationComplete(this.draft); }
  private scope() { return `${this.auth.companyId()}:${this.auth.establishmentId()}:${this.auth.emissionPointId()}:${this.auth.token()}`; }
  canSave() { return this.permission.hasPermission(PERMISSIONS.fiscalSettingsWrite) && this.loadedScope === this.scope(); }
  ngOnChanges() {
    const generation = ++this.generation;
    this.loadedScope = ''; this.draft = this.empty(); this.identity = null; this.current = null; this.error = ''; this.saving = false;
    if (!this.visible || !this.product) return;
    const scope = this.scope(); const id = this.product.id; this.loading = true;
    this.service.product(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: state => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.identity = state;
        const row = state.classification;
        this.loading = false; this.loadedScope = scope; this.current = row?.isCurrent ?? false;
        this.draft.expectedProductRevision = state.productRevision;
        if (row) this.draft = { ...row, expectedRevision: row.revision, expectedProductRevision: state.productRevision, confirmed: false };
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.loading = false; this.error = resolveHttpErrorMessage(error, 'No se pudo cargar la clasificación.');
      },
    });
  }
  save() {
    if (!this.product || !this.canSave() || this.loading || this.saving || !this.draft.confirmed) return;
    const generation = this.generation; const scope = this.scope(); this.saving = true; this.error = '';
    const payload = { ...this.draft, sriMaterialCode: this.draft.taxBasis === 3 ? this.draft.sriMaterialCode : null };
    this.service.declareProduct(this.product.id, payload).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { if (generation === this.generation && scope === this.scope()) { this.saving = false; this.close(false); } },
      error: (error: HttpErrorResponse) => { if (generation === this.generation && scope === this.scope()) { this.saving = false; this.error = resolveHttpErrorMessage(error, 'No se pudo confirmar la clasificación.'); } },
    });
  }
  close(visible: boolean) { if (!this.saving) { this.generation++; this.visibleChange.emit(visible); } }
  private empty(): DeclareProductFiscalClassification {
    return {expectedRevision:0,expectedProductRevision:null,kind:null,activity:null,taxBasis:null,legalReference:'',evidenceReference:'',sriMaterialCode:null,naturalAgriculturalFoodConfirmed:false,foodProcessing:null,constructionMaterialConfirmed:false,confirmed:false};
  }
}
