import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { finalize, switchMap } from 'rxjs';
import { PlatformApi } from './platform-api.service';
import { PlatformStore } from './platform.store';
import { resolveHttpErrorMessage } from '../../core/utils/http-error-normalizer';

@Component({
  selector: 'app-platform-login', standalone: true,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, MessageModule],
  template: `
    <main class="platform-login">
      <form [formGroup]="form" (ngSubmit)="submit()">
        <img src="/hf-one-logo.svg" alt="HF One" width="200" height="56" />
        <h1>HF One Plataforma</h1>
        <p>Administraci\u00f3n de empresas</p>
        <label for="platform-user">Usuario</label>
        <input id="platform-user" pInputText formControlName="username" autocomplete="username" maxlength="100" />
        <label for="platform-password">Contrase\u00f1a</label>
        <input id="platform-password" pInputText type="password" formControlName="password" autocomplete="current-password" />
        @if (error()) { <p-message severity="error" [text]="error()" /> }
        <p-button type="submit" label="Iniciar sesi\u00f3n" icon="pi pi-sign-in" [loading]="loading()" [disabled]="loading()" />
      </form>
    </main>`,
  styleUrl: './platform-controls.scss',
  styles: [`:host{display:block}.platform-login{min-height:100dvh;display:grid;place-items:center;background:#f4f6f8;padding:24px}form{width:min(100%,380px);display:grid;gap:14px}h1{font-size:26px;margin:0}p{margin:0 0 12px;color:#596575}label{font-weight:600;font-size:14px}.pi-building{font-size:32px;color:#087f70}input{width:100%}:host ::ng-deep .p-button{width:100%}`],
})
export class PlatformLogin {
  private readonly api = inject(PlatformApi);
  private readonly store = inject(PlatformStore);
  private readonly router = inject(Router);
  readonly form = inject(FormBuilder).nonNullable.group({ username: ['', Validators.required], password: ['', Validators.required] });
  readonly loading = signal(false);
  readonly error = signal('');
  submit() {
    if (this.loading()) return;
    if (this.form.invalid) { this.form.markAllAsTouched(); this.error.set('Ingresa usuario y contrase\u00f1a.'); return; }
    this.loading.set(true); this.error.set('');
    const value = this.form.getRawValue();
    this.api.login(value.username.trim(), value.password).pipe(switchMap(result => {
      this.store.setToken(result.token);
      return this.store.loadMe();
    }), finalize(() => this.loading.set(false))).subscribe({
      next: me => {
        this.form.controls.password.reset('');
        if (me) this.router.navigate(['/platform/tenants']);
        else this.error.set('No se pudo validar la sesi\u00f3n de plataforma.');
      }, error: error => this.error.set(resolveHttpErrorMessage(error, 'No se pudo iniciar sesi\u00f3n.')),
    });
  }
}
