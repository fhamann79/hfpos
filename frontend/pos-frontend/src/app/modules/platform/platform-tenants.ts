import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule, TableLazyLoadEvent } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { finalize } from 'rxjs';
import { PlatformApi } from './platform-api.service';
import { PlatformStore } from './platform.store';
import { Tenant, TenantDetail, TenantDraft, TenantEvent } from './platform.model';
import { resolveHttpErrorMessage } from '../../core/utils/http-error-normalizer';
import { NEW_PASSWORD_VALIDATORS } from '../../core/security/password-policy';

type TenantReadScope = 'list' | 'detail' | 'events';

@Component({
  selector: 'app-platform-tenants', standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, ButtonModule, DialogModule,
    InputTextModule, MessageModule, SelectModule, TableModule, TagModule],
  templateUrl: './platform-tenants.html', styleUrl: './platform-tenants.scss',
})
export class PlatformTenants implements OnInit, OnDestroy {
  private readonly api = inject(PlatformApi);
  readonly store = inject(PlatformStore);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  readonly tenants = signal<Tenant[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly detailLoading = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly detail = signal<TenantDetail | null>(null);
  readonly events = signal<TenantEvent[]>([]);
  readonly eventTotal = signal(0);
  readonly eventLoading = signal(false);
  search = ''; status: string | null = null; first = 0; rows = 20; eventFirst = 0;
  readonly statuses = [{ label: 'Todos', value: null }, { label: 'Activo', value: 'Active' }, { label: 'Suspendido', value: 'Suspended' }];
  provisionVisible = false; detailVisible = false;
  lifecycleTarget: Tenant | null = null; lifecycleActive = false; reason = '';
  private requestId: string | null = null;
  private listSequence = 0;
  private detailSequence = 0;
  private eventSequence = 0;
  // Only mutation/validation errors supersede independent GET error callbacks.
  private errorSequence = 0;
  private protectedError = '';
  private readErrorOrder = 0;
  private readonly readErrors: Record<TenantReadScope, { message: string; order: number }> = {
    list: { message: '', order: 0 }, detail: { message: '', order: 0 }, events: { message: '', order: 0 },
  };
  private selectedDetailId: number | null = null;
  private destroyed = false;
  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    ruc: ['', [Validators.required, Validators.pattern(/^[0-9]{13}$/)]],
    timeZoneId: ['America/Guayaquil', [Validators.required, Validators.maxLength(100)]],
    establishment: ['Matriz', [Validators.required, Validators.maxLength(150)]],
    address: ['', [Validators.required, Validators.maxLength(250)]], emissionPoint: ['Caja Principal', [Validators.required, Validators.maxLength(150)]],
    establishmentCode: ['001', [Validators.required, Validators.pattern(/^(?!000)[0-9]{3}$/)]],
    emissionPointCode: ['001', [Validators.required, Validators.pattern(/^(?!000)[0-9]{3}$/)]],
    username: ['', [Validators.required, Validators.maxLength(100)]], email: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
    password: ['', NEW_PASSWORD_VALIDATORS],
  });
  constructor() {
    this.form.valueChanges.subscribe(() => { this.requestId = null; });
  }
  ngOnInit() { this.load(); }
  ngOnDestroy() {
    this.destroyed = true; ++this.listSequence; this.loading.set(false); this.closeDetail();
  }
  load() {
    if (this.destroyed) return;
    const sequence = ++this.listSequence;
    this.loading.set(true); this.setReadError('list', '');
    const errorSequence = this.errorSequence;
    this.api.tenants(this.search, this.status, Math.floor(this.first / this.rows) + 1, this.rows)
      .pipe(finalize(() => { if (sequence === this.listSequence) this.loading.set(false); })).subscribe({
        next: page => { if (sequence !== this.listSequence) return; this.tenants.set(page.items); this.total.set(page.totalItems); },
        error: error => { if (sequence === this.listSequence && errorSequence === this.errorSequence) this.fail(error, 'list'); },
      });
  }
  filter() { this.first = 0; this.load(); }
  page(event: TableLazyLoadEvent) { this.first = event.first ?? 0; this.rows = event.rows ?? 20; this.load(); }
  openProvision() {
    this.form.reset({ name: '', ruc: '', timeZoneId: 'America/Guayaquil', establishment: 'Matriz', address: '',
      emissionPoint: 'Caja Principal', establishmentCode: '001', emissionPointCode: '001', username: '', email: '', password: '' });
    this.setError(''); this.provisionVisible = true;
  }
  closeProvision() { if (!this.saving()) { this.form.controls.password.reset(''); this.requestId = null; } }
  provision() {
    if (this.saving()) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); this.setError('Revisa los campos requeridos, RUC, email y contrase\u00f1a (m\u00ednimo 12 caracteres).'); return; }
    const value = this.form.getRawValue();
    const draft: TenantDraft = { company: { name: value.name, ruc: value.ruc, timeZoneId: value.timeZoneId },
      initialEstablishment: { name: value.establishment, address: value.address, code: value.establishmentCode },
      initialEmissionPoint: { name: value.emissionPoint, code: value.emissionPointCode },
      initialAdmin: { username: value.username, email: value.email, password: value.password } };
    this.requestId ??= crypto.randomUUID();
    this.saving.set(true); this.setError('');
    this.api.provision({ ...draft, requestId: this.requestId }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: result => {
        this.form.controls.password.reset(''); this.requestId = null; this.provisionVisible = false;
        this.notice.set(`Empresa ${result.tenant.company.name} creada.`); this.first = 0; this.load(); this.openDetail(result.tenant.company);
      }, error: error => this.fail(error),
    });
  }
  openDetail(tenant: Tenant) {
    if (this.destroyed) return;
    this.closeDetail();
    this.selectedDetailId = tenant.id;
    const sequence = ++this.detailSequence;
    this.detailVisible = true; this.detailLoading.set(true);
    const errorSequence = this.errorSequence;
    this.api.detail(tenant.id).pipe(finalize(() => { if (sequence === this.detailSequence) this.detailLoading.set(false); })).subscribe({
      next: detail => { if (sequence !== this.detailSequence) return; this.detail.set(detail); this.eventFirst = 0; this.loadEvents(undefined, errorSequence); },
      error: error => { if (sequence === this.detailSequence && errorSequence === this.errorSequence) this.fail(error, 'detail'); },
    });
  }
  closeDetail() {
    ++this.detailSequence; ++this.eventSequence; this.selectedDetailId = null; this.detailVisible = false;
    this.detail.set(null); this.events.set([]); this.eventTotal.set(0); this.eventFirst = 0;
    this.detailLoading.set(false); this.eventLoading.set(false);
    this.setReadError('detail', ''); this.setReadError('events', '');
  }
  loadEvents(event?: TableLazyLoadEvent, errorSequence = this.errorSequence) {
    const id = this.detail()?.company.id; if (this.destroyed || !this.detailVisible || !id) return;
    const sequence = ++this.eventSequence;
    this.setReadError('events', '');
    this.eventFirst = event?.first ?? this.eventFirst; this.eventLoading.set(true);
    this.api.events(id, Math.floor(this.eventFirst / 10) + 1, 10).pipe(finalize(() => { if (sequence === this.eventSequence) this.eventLoading.set(false); }))
      .subscribe({
        next: page => { if (sequence !== this.eventSequence) return; this.events.set(page.items); this.eventTotal.set(page.totalItems); },
        error: error => { if (sequence === this.eventSequence && errorSequence === this.errorSequence) this.fail(error, 'events'); },
      });
  }
  openLifecycle(tenant: Tenant, active: boolean) { this.lifecycleTarget = tenant; this.lifecycleActive = active; this.reason = ''; this.setError(''); }
  lifecycle() {
    if (this.saving() || !this.lifecycleTarget) return;
    if (!this.reason.trim() || this.reason.trim().length > 500) { this.setError('Ingresa un motivo de hasta 500 caracteres.'); return; }
    const id = this.lifecycleTarget.id; this.saving.set(true);
    this.api.setActive(id, this.lifecycleActive, this.reason.trim()).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: detail => { this.lifecycleTarget = null; this.notice.set(detail.company.isActive ? 'Empresa reactivada.' : 'Empresa suspendida.');
        this.setError(''); this.load(); if (!this.destroyed && this.detailVisible && this.selectedDetailId === id) {
          // The mutation result supersedes even a still-pending detail GET for this selection.
          ++this.detailSequence; this.detailLoading.set(false);
          this.setReadError('detail', '');
          this.detail.set(detail); this.eventFirst = 0; this.loadEvents();
        } },
      error: error => this.fail(error),
    });
  }
  eventLabel(type: TenantEvent['eventType']) { return { Provisioned: 'Provisionado', Suspended: 'Suspendido', Reactivated: 'Reactivado' }[type]; }
  logout() { this.store.clear(); this.router.navigate(['/platform/login']); }
  private setError(message: string) {
    if (message) ++this.errorSequence;
    this.protectedError = message; this.refreshError();
  }
  private setReadError(scope: TenantReadScope, message: string) {
    this.readErrors[scope] = { message, order: message ? ++this.readErrorOrder : 0 };
    this.refreshError();
  }
  private refreshError() {
    const latest = Object.values(this.readErrors).reduce((current, candidate) => candidate.order > current.order ? candidate : current);
    this.error.set(this.protectedError || latest.message);
  }
  private fail(error: unknown, scope?: TenantReadScope) {
    const message = error instanceof HttpErrorResponse
      ? resolveHttpErrorMessage(error, 'No se pudo completar la operaci\u00f3n.')
      : 'No se pudo completar la operaci\u00f3n.';
    if (scope) this.setReadError(scope, message); else this.setError(message);
  }
}
