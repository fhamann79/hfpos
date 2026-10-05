import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { PasswordRecoveryApi, RecoveryAccount } from '../../core/services/password-recovery.service';
import { RecoveryDialog } from '../auth/recovery/recovery-dialog';
import { PlatformStore } from './platform.store';

@Component({
  selector: 'app-platform-accounts', standalone: true,
  imports: [RouterLink, ButtonModule, MessageModule, TableModule, RecoveryDialog],
  template: `<main class="platform-page"><header class="platform-header"><div><small>HF One Plataforma</small><h1>Cuentas de plataforma</h1></div>
    <a routerLink="/platform/tenants">Empresas</a><a routerLink="/platform/account/password">Cambiar mi contrase\u00f1a</a></header>
    @if (error()) { <p-message severity="error" [text]="error()" /> }
    <p-table [value]="accounts()" [loading]="loading()" responsiveLayout="scroll">
      <ng-template #header><tr><th>Usuario</th><th>Estado</th><th>Acciones</th></tr></ng-template>
      <ng-template #body let-user><tr><td>{{ user.username }}</td><td>{{ user.isActive ? 'Activo' : 'Inactivo' }}</td>
        <td>@if (user.isActive && user.id !== store.me()?.id) {
          <p-button label="Iniciar recuperaci\u00f3n" icon="pi pi-key" [text]="true" (onClick)="selected.set(user)" />
        }</td></tr></ng-template>
      <ng-template #emptymessage><tr><td colspan="3">No hay cuentas disponibles.</td></tr></ng-template>
    </p-table>
    @if (selected(); as user) { <app-recovery-dialog [platform]="true" [visible]="true" [userId]="user.id" [username]="user.username" (visibleChange)="selected.set(null)" /> }
  </main>`, styleUrl: './platform-tenants.scss',
})
export class PlatformAccounts implements OnInit {
  private readonly api = inject(PasswordRecoveryApi); readonly store = inject(PlatformStore);
  readonly accounts = signal<RecoveryAccount[]>([]); readonly loading = signal(true); readonly error = signal('');
  readonly selected = signal<RecoveryAccount | null>(null);
  ngOnInit() { this.api.accounts().subscribe({ next: users => { this.accounts.set(users); this.loading.set(false); },
    error: () => { this.loading.set(false); this.error.set('No se pudieron cargar las cuentas.'); } }); }
}
