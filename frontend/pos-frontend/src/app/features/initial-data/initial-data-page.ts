import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { forkJoin } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../core/utils/http-error-normalizer';
import { InitialDataService } from './initial-data.service';
import { InitialDataKind, InitialDataPayload, InitialDataPreview, InitialDataResult, TenantReadiness } from './initial-data.model';

@Component({
  standalone: true, selector: 'app-initial-data-page',
  imports: [CommonModule, FormsModule, RouterLink, ButtonModule, MessageModule, SelectModule, TableModule, TagModule],
  templateUrl: './initial-data-page.html', styleUrl: './initial-data-page.scss',
})
export class InitialDataPage implements OnInit, OnDestroy {
  private readonly api = inject(InitialDataService);
  private readonly permissions = inject(PermissionService);
  readonly readiness = signal<TenantReadiness | null>(null);
  readonly batches = signal<InitialDataResult[]>([]);
  readonly preview = signal<InitialDataPreview | null>(null);
  readonly result = signal<InitialDataResult | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly fileName = signal('');
  readonly kinds = computed(() => [
    { label: 'Categorias', value: 'categories' as InitialDataKind, permissions: ['CATALOG_CATEGORIES_READ', 'CATALOG_CATEGORIES_WRITE'] },
    { label: 'Productos', value: 'products' as InitialDataKind, permissions: ['CATALOG_PRODUCTS_READ', 'CATALOG_PRODUCTS_WRITE', 'CATALOG_CATEGORIES_READ'] },
    { label: 'Clientes', value: 'customers' as InitialDataKind, permissions: ['CUSTOMERS_READ', 'CUSTOMERS_WRITE'] },
    { label: 'Proveedores', value: 'suppliers' as InitialDataKind, permissions: ['SUPPLIERS_READ', 'SUPPLIERS_WRITE'] },
    { label: 'Inventario de apertura', value: 'opening-inventory' as InitialDataKind, permissions: ['INVENTORY_READ', 'INVENTORY_WRITE', 'CATALOG_PRODUCTS_READ'] },
  ].filter(k => this.permissions.hasAllPermissions(k.permissions)));
  readonly labels: Record<string, string> = {
    company: 'Identidad fiscal y direccion matriz', structure: 'Establecimiento y punto', users: 'Administrador y contexto',
    categories: 'Categorias', products: 'Productos', customers: 'Clientes', suppliers: 'Proveedores',
    'opening-inventory': 'Inventario de apertura', 'fiscal-configuration': 'Configuracion fiscal y vigencia registrada',
  };
  kind: InitialDataKind = 'categories';
  historyPage = 1;
  private payload: InitialDataPayload | null = null;
  private sequence = 0;
  private readSequence = 0;
  private destroyed = false;
  ngOnInit() { this.kind = this.kinds()[0]?.value ?? 'categories'; this.refresh(); }
  ngOnDestroy() { this.destroyed = true; ++this.sequence; ++this.readSequence; }
  refresh() {
    const sequence = ++this.readSequence;
    this.loading.set(true);
    forkJoin({ readiness: this.api.readiness(), batches: this.api.batches(this.historyPage) }).subscribe({
      next: data => {
        if (sequence !== this.readSequence) return;
        this.readiness.set(data.readiness); this.batches.set(data.batches); this.loading.set(false);
      },
      error: error => { if (sequence === this.readSequence) { this.loading.set(false); this.fail(error); } },
    });
  }
  reset() {
    if (this.saving()) return;
    ++this.sequence; this.payload = null; this.preview.set(null); this.result.set(null); this.fileName.set(''); this.error.set('');
  }
  async selectFile(event: Event) {
    if (this.saving()) return;
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0]; input.value = '';
    this.reset(); if (!file) return;
    if (file.size > 1048576) { this.error.set('El archivo supera 1 MiB.'); return; }
    const sequence = this.sequence;
    try {
      const csv = await file.text();
      if (sequence !== this.sequence || this.destroyed) return;
      this.fileName.set(file.name);
      this.payload = { requestId: crypto.randomUUID(), kind: this.kind, csv, duplicatePolicy: 'create-only' };
    } catch { if (sequence === this.sequence) this.error.set('No se pudo leer el archivo.'); }
  }
  showPreview() {
    if (!this.payload || this.saving()) return;
    const sequence = ++this.sequence;
    this.preview.set(null); this.result.set(null); this.error.set(''); this.saving.set(true);
    this.api.preview(this.payload).subscribe({
      next: preview => { if (sequence === this.sequence) { this.preview.set(preview); this.saving.set(false); } },
      error: error => { if (sequence === this.sequence) { this.saving.set(false); this.fail(error); } },
    });
  }
  confirm() {
    const token = this.preview()?.previewToken;
    if (!this.payload || !token || !this.preview()?.canConfirm || this.saving() || this.result()) return;
    const sequence = ++this.sequence;
    this.error.set(''); this.saving.set(true);
    this.api.confirm(this.payload, token).subscribe({
      next: result => {
        if (sequence !== this.sequence) return;
        this.saving.set(false); this.result.set(result); this.historyPage = 1; this.refresh();
      },
      // Preserve payload/request/token after ambiguous HTTP failure for the same retry.
      error: error => { if (sequence === this.sequence) { this.saving.set(false); this.fail(error); } },
    });
  }
  downloadTemplate() {
    const kind = this.kind;
    this.api.template(kind).subscribe({
      next: blob => {
        if (this.destroyed) return;
        const url = URL.createObjectURL(blob); const a = document.createElement('a');
        a.href = url; a.download = `${kind}.csv`; a.click(); URL.revokeObjectURL(url);
      }, error: error => this.fail(error),
    });
  }
  page(delta: number) { this.historyPage = Math.max(1, this.historyPage + delta); this.refresh(); }
  hasFile() { return this.payload !== null; }
  values(row: Record<string, string>) { return Object.values(row).join(' | '); }
  private fail(error: unknown) {
    this.error.set(error instanceof HttpErrorResponse
      ? resolveHttpErrorMessage(error, 'No se pudo completar la operacion.') : 'No se pudo completar la operacion.');
  }
}
