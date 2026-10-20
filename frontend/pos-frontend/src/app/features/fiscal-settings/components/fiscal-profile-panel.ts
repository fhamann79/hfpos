import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { PERMISSIONS } from '../../../core/constants/permissions';
import { PermissionService } from '../../../core/services/permission.service';
import { AuthStore } from '../../../core/stores/auth.store';
import { resolveHttpErrorMessage } from '../../../core/utils/http-error-normalizer';
import { DeclareFiscalProfile, FiscalProfile, FISCAL_REGIMES } from '../models/fiscal-profile.model';
import { FiscalProfileService } from '../services/fiscal-profile.service';

@Component({
  selector: 'app-fiscal-profile-panel', standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, InputTextModule, SelectModule, MessageModule, TableModule],
  templateUrl: './fiscal-profile-panel.html', styleUrl: './fiscal-profile-panel.scss',
})
export class FiscalProfilePanel {
  private readonly service = inject(FiscalProfileService);
  private readonly auth = inject(AuthStore);
  private readonly permission = inject(PermissionService);
  private readonly destroyRef = inject(DestroyRef);
  private generation = 0;
  private loadedScope = '';
  readonly history = signal<FiscalProfile[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  readonly regimes = FISCAL_REGIMES;
  readonly kinds = [{ label: 'Persona natural', value: 0 }, { label: 'Persona jurídica', value: 1 }];
  draft: DeclareFiscalProfile = this.emptyDraft();
  canWrite() { return this.permission.hasPermission(PERMISSIONS.fiscalSettingsWrite); }
  private scope() { return `${this.auth.companyId()}:${this.auth.establishmentId()}:${this.auth.emissionPointId()}:${this.auth.token()}`; }
  private readonly contextEffect = effect(() => { this.scope(); untracked(() => this.reload()); });

  reload() {
    const generation = ++this.generation;
    const scope = this.scope();
    this.loadedScope = ''; this.history.set([]); this.draft = this.emptyDraft();
    this.loading.set(true); this.saving.set(false); this.error.set(''); this.success.set('');
    this.service.history().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: rows => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.history.set(rows); this.loadedScope = scope; this.loading.set(false);
        this.draft.expectedRevision = rows[0]?.revision ?? 0;
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.loading.set(false); this.error.set(resolveHttpErrorMessage(error, 'No se pudo cargar el perfil fiscal.'));
      },
    });
  }
  save() {
    if (!this.canWrite() || this.loading() || this.saving() || this.loadedScope !== this.scope()) return;
    if (this.draft.regime === null || this.draft.taxpayerKind === null || !this.draft.confirmed
      || !this.draft.reason.trim() || !this.draft.evidenceReference.trim() || !this.draft.effectiveFrom) {
      this.error.set('Completa los datos y confirma la declaración fiscal.'); return;
    }
    const scope = this.scope(); const generation = this.generation;
    this.saving.set(true); this.error.set('');
    this.service.declare({ ...this.draft }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.reload(); this.success.set('Versión fiscal registrada. Se aplicará a nuevas operaciones según su vigencia.');
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation || scope !== this.scope()) return;
        this.saving.set(false); this.error.set(resolveHttpErrorMessage(error, 'No se pudo confirmar el perfil. Recarga antes de reintentar.'));
      },
    });
  }
  label(value: number | null) { return this.regimes.find(r => r.value === value)?.label ?? 'Sin confirmar'; }
  activeProfile() {
    const today = this.emptyDraft().effectiveFrom;
    return this.history().find(p => p.effectiveFrom <= today && (!p.effectiveUntil || today < p.effectiveUntil));
  }
  private emptyDraft(): DeclareFiscalProfile {
    return { expectedRevision: 0, regime: null, taxpayerKind: null, isAccountingRequired: false,
      specialTaxpayerResolution: null, withholdingAgentResolution: null,
      effectiveFrom: new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Guayaquil', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date()),
      effectiveUntil: null, evidenceReference: '', reason: '', confirmed: false, rimpeEligibilityConfirmed: false };
  }
}
