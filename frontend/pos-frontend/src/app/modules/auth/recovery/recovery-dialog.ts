import { CommonModule } from '@angular/common';
import { Component, Input, OnChanges, OnDestroy, Output, EventEmitter, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { finalize } from 'rxjs';
import { PasswordRecoveryApi, RecoveryStatus } from '../../../core/services/password-recovery.service';

@Component({
  selector: 'app-recovery-dialog', standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, CheckboxModule, DialogModule, InputTextModule, MessageModule],
  template: `<p-dialog header="Recuperaci\u00f3n asistida" [visible]="visible" (visibleChange)="close()" [modal]="true"
    [closable]="!busy()" [closeOnEscape]="!busy()" [style]="{width: '520px', maxWidth: '96vw'}">
    <p><strong>{{ username }}</strong></p>
    <form class="recovery-form" [formGroup]="form" (ngSubmit)="issue()">
      <label for="recovery-reason">Motivo</label><input id="recovery-reason" pInputText formControlName="reason" maxlength="500" />
      <label for="recovery-delivery">Referencia del canal acreditado</label><input id="recovery-delivery" pInputText formControlName="deliveryReference" maxlength="500" />
      <label class="identity"><p-checkbox inputId="identity-confirmed" formControlName="identityConfirmed" [binary]="true" />
        <span>Verifiqu\u00e9 la identidad mediante un procedimiento previamente acreditado. El email guardado no es prueba de identidad.</span></label>
      @if (platform) {
        <label for="recovery-current">Tu contrase\u00f1a actual</label><input id="recovery-current" pInputText type="password" formControlName="currentPassword" autocomplete="current-password" maxlength="256" />
      }
      @if (error()) { <p-message severity="error" [text]="error()" /> }
      @if (!link()) { <p-button type="submit" label="Generar enlace" icon="pi pi-key" [loading]="busy()" [disabled]="busy()" /> }
    </form>
    @if (link()) {
      <div class="generated-link"><label for="one-time-link">Enlace confidencial, visible solo ahora</label>
        <input id="one-time-link" pInputText [value]="link()" readonly autocomplete="off" />
        <small>Vence {{ expiresAt() | date:'HH:mm' }}. Comp\u00e1rtelo por el canal acreditado. El titular elige su contrase\u00f1a.</small>
        <p-button label="Copiar enlace" icon="pi pi-copy" (onClick)="copy()" />
        @if (notice()) { <p-message severity="success" [text]="notice()" /> }
      </div>
    }
    <ul class="challenge-list">
      @for (challenge of challenges(); track challenge.id) {
        <li><span>{{ challenge.expiresAt | date:'dd/MM HH:mm' }} · {{ state(challenge) }}</span>
          @if (!challenge.revokedAt && !challenge.consumedAt && isLive(challenge)) {
            <p-button label="Revocar" icon="pi pi-ban" severity="danger" [text]="true" [disabled]="busy()" (onClick)="revoke(challenge)" />
          }
        </li>
      }
    </ul>
  </p-dialog>`,
  styles: [`.recovery-form,.generated-link{display:grid;gap:12px}.generated-link{margin-top:20px;padding-top:16px;border-top:1px solid #dce3e0}input{width:100%;min-width:0}label{font-size:14px;font-weight:600}.identity{display:flex;gap:10px;font-weight:400;line-height:1.5}.challenge-list{padding:0;list-style:none}.challenge-list li{display:flex;justify-content:space-between;align-items:center;gap:8px;border-top:1px solid #e5eae7;padding:8px 0;font-size:14px}small{color:#5d6963;line-height:1.5}`],
})
export class RecoveryDialog implements OnChanges, OnDestroy {
  @Input() visible = false; @Input() platform = false; @Input() userId = 0; @Input() username = '';
  @Output() visibleChange = new EventEmitter<boolean>();
  private readonly api = inject(PasswordRecoveryApi);
  readonly form = inject(FormBuilder).nonNullable.group({ reason: ['', [Validators.required, Validators.maxLength(500)]],
    deliveryReference: ['', [Validators.required, Validators.maxLength(500)]], identityConfirmed: [false, Validators.requiredTrue], currentPassword: [''] });
  readonly link = signal(''); readonly expiresAt = signal(''); readonly busy = signal(false);
  readonly error = signal(''); readonly notice = signal(''); readonly challenges = signal<RecoveryStatus[]>([]);
  private sequence = 0;
  ngOnChanges() {
    ++this.sequence; this.clear();
    this.form.controls.currentPassword.setValidators(this.platform ? [Validators.required, Validators.maxLength(256)] : []);
    this.form.controls.currentPassword.updateValueAndValidity();
    if (this.visible && this.userId) this.load();
  }
  private load() {
    const sequence = this.sequence;
    this.api.status(this.platform, this.userId).subscribe({ next: value => { if (sequence === this.sequence) this.challenges.set(value); },
      error: () => { if (sequence === this.sequence) this.error.set('No se pudieron cargar los enlaces.'); } });
  }
  issue() {
    if (this.busy() || this.link()) return;
    if (this.form.invalid || !this.form.controls.reason.value.trim() || !this.form.controls.deliveryReference.value.trim()) {
      this.form.markAllAsTouched(); this.error.set('Completa el motivo, la referencia y la verificaci\u00f3n de identidad.'); return;
    }
    const sequence = this.sequence; this.busy.set(true); this.error.set('');
    this.api.issue(this.platform, this.userId, this.form.getRawValue()).pipe(finalize(() => { if (sequence === this.sequence) this.busy.set(false); })).subscribe({
      next: value => {
        if (sequence !== this.sequence) return;
        this.link.set(`${location.origin}/${this.platform ? 'platform/' : ''}recover-access/complete#token=${value.token}`);
        this.expiresAt.set(value.expiresAt); this.form.controls.currentPassword.reset(''); this.load();
      }, error: () => { if (sequence === this.sequence) { this.form.controls.currentPassword.reset(''); this.error.set('No se pudo iniciar la recuperaci\u00f3n. Revisa tu autoridad y los datos.'); } },
    });
  }
  revoke(challenge: RecoveryStatus) {
    if (this.busy()) return;
    if (this.platform && !this.form.controls.currentPassword.value) { this.error.set('Ingresa tu contrase\u00f1a actual para revocar.'); return; }
    const sequence = this.sequence; this.busy.set(true); this.error.set('');
    this.api.revoke(this.platform, challenge.id, this.form.controls.currentPassword.value).pipe(finalize(() => { if (sequence === this.sequence) this.busy.set(false); })).subscribe({
      next: () => { if (sequence !== this.sequence) return; this.link.set(''); this.form.controls.currentPassword.reset(''); this.load(); },
      error: () => { if (sequence === this.sequence) { this.form.controls.currentPassword.reset(''); this.error.set('No se pudo revocar el enlace.'); } },
    });
  }
  async copy() {
    const sequence = this.sequence;
    try { await navigator.clipboard.writeText(this.link()); if (sequence === this.sequence) this.notice.set('Enlace copiado.'); }
    catch { if (sequence === this.sequence) this.error.set('No se pudo copiar. Selecciona el enlace para copiarlo.'); }
  }
  isLive(c: RecoveryStatus) { return Date.parse(c.expiresAt) > Date.now(); }
  state(c: RecoveryStatus) { return c.consumedAt ? 'Usado' : c.revokedAt ? 'Revocado' : this.isLive(c) ? 'Disponible' : 'Vencido'; }
  close() { if (this.busy()) return; ++this.sequence; this.clear(); this.visibleChange.emit(false); }
  private clear() { this.link.set(''); this.expiresAt.set(''); this.error.set(''); this.notice.set(''); this.challenges.set([]); this.busy.set(false); this.form.reset(); }
  ngOnDestroy() { ++this.sequence; this.clear(); }
}
