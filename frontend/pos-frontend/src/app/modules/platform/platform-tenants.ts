import { Component, OnInit, inject, signal } from '@angular/core';
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

@Component({
  selector: 'app-platform-tenants', standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, ButtonModule, DialogModule,
    InputTextModule, MessageModule, SelectModule, TableModule, TagModule],
  templateUrl: './platform-tenants.html', styleUrl: './platform-tenants.scss',
})
export class PlatformTenants implements OnInit {
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
  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    ruc: ['', [Validators.required, Validators.pattern(/^[0-9]{13}$/)]],
    timeZoneId: ['America/Guayaquil', [Validators.required, Validators.maxLength(100)]],
    establishment: ['Matriz', [Validators.required, Validators.maxLength(150)]],
    address: ['', Validators.maxLength(250)], emissionPoint: ['Caja Principal', [Validators.required, Validators.maxLength(150)]],
    username: ['', [Validators.required, Validators.maxLength(100)]], email: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(256)]],
  });
  constructor() {
    this.form.valueChanges.subscribe(() => { this.requestId = null; });
  }
  ngOnInit() { this.load(); }
  load() {
    this.loading.set(true); this.error.set('');
    this.api.tenants(this.search, this.status, Math.floor(this.first / this.rows) + 1, this.rows)
      .pipe(finalize(() => this.loading.set(false))).subscribe({ next: page => { this.tenants.set(page.items); this.total.set(page.totalItems); }, error: error => this.fail(error) });
  }
  filter() { this.first = 0; this.load(); }
  page(event: TableLazyLoadEvent) { this.first = event.first ?? 0; this.rows = event.rows ?? 20; this.load(); }
  openProvision() {
    this.form.reset({ name: '', ruc: '', timeZoneId: 'America/Guayaquil', establishment: 'Matriz', address: '',
      emissionPoint: 'Caja Principal', username: '', email: '', password: '' });
    this.error.set(''); this.provisionVisible = true;
  }
  closeProvision() { if (!this.saving()) { this.form.controls.password.reset(''); this.requestId = null; } }
  provision() {
    if (this.saving()) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Revisa los campos requeridos, RUC, email y contrase\u00f1a (m\u00ednimo 12 caracteres).'); return; }
    const value = this.form.getRawValue();
    const draft: TenantDraft = { company: { name: value.name, ruc: value.ruc, timeZoneId: value.timeZoneId },
      initialEstablishment: { name: value.establishment, address: value.address }, initialEmissionPoint: { name: value.emissionPoint },
      initialAdmin: { username: value.username, email: value.email, password: value.password } };
    this.requestId ??= crypto.randomUUID();
    this.saving.set(true); this.error.set('');
    this.api.provision({ ...draft, requestId: this.requestId }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: result => {
        this.form.controls.password.reset(''); this.requestId = null; this.provisionVisible = false;
        this.notice.set(`Empresa ${result.tenant.company.name} creada.`); this.first = 0; this.load(); this.openDetail(result.tenant.company);
      }, error: error => this.fail(error),
    });
  }
  openDetail(tenant: Tenant) {
    this.detail.set(null); this.events.set([]); this.detailVisible = true; this.detailLoading.set(true);
    this.api.detail(tenant.id).pipe(finalize(() => this.detailLoading.set(false))).subscribe({
      next: detail => { this.detail.set(detail); this.eventFirst = 0; this.loadEvents(); }, error: error => this.fail(error),
    });
  }
  loadEvents(event?: TableLazyLoadEvent) {
    const id = this.detail()?.company.id; if (!id) return;
    this.eventFirst = event?.first ?? this.eventFirst; this.eventLoading.set(true);
    this.api.events(id, Math.floor(this.eventFirst / 10) + 1, 10).pipe(finalize(() => this.eventLoading.set(false)))
      .subscribe({ next: page => { this.events.set(page.items); this.eventTotal.set(page.totalItems); }, error: error => this.fail(error) });
  }
  openLifecycle(tenant: Tenant, active: boolean) { this.lifecycleTarget = tenant; this.lifecycleActive = active; this.reason = ''; this.error.set(''); }
  lifecycle() {
    if (this.saving() || !this.lifecycleTarget) return;
    if (!this.reason.trim() || this.reason.trim().length > 500) { this.error.set('Ingresa un motivo de hasta 500 caracteres.'); return; }
    const id = this.lifecycleTarget.id; this.saving.set(true);
    this.api.setActive(id, this.lifecycleActive, this.reason.trim()).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: detail => { this.lifecycleTarget = null; this.notice.set(detail.company.isActive ? 'Empresa reactivada.' : 'Empresa suspendida.');
        this.load(); if (this.detailVisible && this.detail()?.company.id === id) { this.detail.set(detail); this.eventFirst = 0; this.loadEvents(); } },
      error: error => this.fail(error),
    });
  }
  eventLabel(type: TenantEvent['eventType']) { return { Provisioned: 'Provisionado', Suspended: 'Suspendido', Reactivated: 'Reactivado' }[type]; }
  logout() { this.store.clear(); this.router.navigate(['/platform/login']); }
  private fail(error: unknown) {
    this.error.set(error instanceof HttpErrorResponse
      ? resolveHttpErrorMessage(error, 'No se pudo completar la operaci\u00f3n.')
      : 'No se pudo completar la operaci\u00f3n.');
  }
}
