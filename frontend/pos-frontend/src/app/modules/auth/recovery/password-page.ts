import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { finalize } from 'rxjs';
import { PasswordRecoveryApi } from '../../../core/services/password-recovery.service';
import { NEW_PASSWORD_VALIDATORS } from '../../../core/security/password-policy';
import { AuthStore } from '../../../core/stores/auth.store';
import { PlatformStore } from '../../platform/platform.store';

@Component({
  selector: 'app-password-page', standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, MessageModule, PasswordModule],
  template: `<main class="login-page"><section class="access-panel">
    <img class="brand-logo" src="/hf-one-logo.svg" alt="HF One" width="200" height="56" />
    <h1>{{ self ? 'Cambiar contrase\u00f1a' : 'Nueva contrase\u00f1a' }}</h1>
    <p class="access-subtitle">{{ platform ? 'Acceso de plataforma' : 'Acceso a tu empresa' }}</p>
    @if (success()) {
      <p-message severity="success" text="Contrase\u00f1a actualizada. Inicia sesi\u00f3n con tu nueva contrase\u00f1a." />
    } @else {
      <form class="login-form" [formGroup]="form" (ngSubmit)="submit()">
        @if (self) {
          <div class="field"><label for="current-password">Contrase\u00f1a actual</label>
          <p-password inputId="current-password" formControlName="currentPassword" [feedback]="false" [toggleMask]="true" autocomplete="current-password" [maxlength]="256" /></div>
        }
        <div class="field"><label for="new-password">Nueva contrase\u00f1a</label>
        <p-password inputId="new-password" formControlName="newPassword" [feedback]="false" [toggleMask]="true" autocomplete="new-password" [maxlength]="256" />
        <small>De 12 a 256 caracteres.</small></div>
        <div class="field"><label for="confirm-password">Confirmar contrase\u00f1a</label>
        <p-password inputId="confirm-password" formControlName="confirmation" [feedback]="false" [toggleMask]="true" autocomplete="new-password" [maxlength]="256" /></div>
        @if (error()) { <p-message severity="error" [text]="error()" /> }
        <p-button type="submit" label="Guardar contrase\u00f1a" icon="pi pi-check" [loading]="busy()" [disabled]="busy() || (!self && !hasToken)" />
      </form>
    }
    <a class="access-back" [routerLink]="platform ? '/platform/login' : '/login'">Volver a iniciar sesi\u00f3n</a>
  </section></main>`, styleUrl: '../login/login.scss',
})
export class PasswordPage implements OnDestroy {
  private readonly api = inject(PasswordRecoveryApi);
  private readonly tenantStore = inject(AuthStore);
  private readonly platformStore = inject(PlatformStore);
  private readonly route = inject(ActivatedRoute);
  readonly platform = this.route.snapshot.data['platform'] === true;
  readonly self = this.route.snapshot.data['self'] === true;
  private token = this.self ? '' : new URLSearchParams(this.route.snapshot.fragment ?? '').get('token') ?? '';
  readonly hasToken = /^[tp]\.[0-9a-f]{32}\.[0-9A-F]{64}$/.test(this.token);
  readonly busy = signal(false); readonly error = signal(''); readonly success = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: ['', this.self ? [Validators.required, Validators.maxLength(256)] : []],
    newPassword: ['', NEW_PASSWORD_VALIDATORS], confirmation: ['', NEW_PASSWORD_VALIDATORS],
  });
  private readonly untilDestroyed = takeUntilDestroyed<void>();
  constructor() {
    // The secret lives only in component memory, never browser history, storage or API URLs.
    if (!this.self) inject(Location).replaceState(location.pathname);
    if (!this.self && !this.hasToken) this.error.set('El enlace no es v\u00e1lido. Solicita uno nuevo al operador autorizado.');
  }
  submit() {
    if (this.busy() || (!this.self && !this.hasToken)) return;
    const value = this.form.getRawValue();
    if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Revisa los campos. La nueva contrase\u00f1a debe tener entre 12 y 256 caracteres.'); return; }
    if (value.newPassword !== value.confirmation) { this.error.set('Las contrase\u00f1as no coinciden.'); return; }
    this.busy.set(true); this.error.set('');
    const operation = this.self ? this.api.change(this.platform, value.currentPassword, value.newPassword)
      : this.api.complete(this.platform, this.token, value.newPassword);
    operation.pipe(this.untilDestroyed, finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.token = ''; this.form.reset(); this.success.set(true);
        if (this.self) { if (this.platform) this.platformStore.clear(); else this.tenantStore.clear(); }
      },
      error: (error: HttpErrorResponse) => {
        this.form.controls.currentPassword.reset('');
        this.form.controls.newPassword.reset(''); this.form.controls.confirmation.reset('');
        this.error.set(error.error?.error === 'CURRENT_PASSWORD_INVALID' ? 'La contrase\u00f1a actual no es correcta.'
          : error.status === 429 ? 'Espera un momento antes de volver a intentar.'
          : this.self ? 'No se pudo cambiar la contrase\u00f1a.' : 'El enlace no es v\u00e1lido o ya no est\u00e1 disponible. Solicita uno nuevo.');
      },
    });
  }
  ngOnDestroy() { this.token = ''; this.form.reset(); }
}
