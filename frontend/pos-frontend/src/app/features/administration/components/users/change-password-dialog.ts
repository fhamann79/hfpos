import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { ChangeUserPasswordRequest, User } from '../../models/user.model';

export interface ChangePasswordDialogSubmit {
  userId: number;
  payload: ChangeUserPasswordRequest;
}

@Component({
  selector: 'app-change-password-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, ButtonModule],
  templateUrl: './change-password-dialog.html',
  styleUrl: './change-password-dialog.scss',
})
export class ChangePasswordDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() user: User | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<ChangePasswordDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(6)]],
    confirmPassword: ['', [Validators.required]],
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.form.reset({ newPassword: '', confirmPassword: '' });
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  save(): void {
    if (!this.user) {
      return;
    }

    if (this.form.invalid || this.passwordMismatch) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitForm.emit({
      userId: this.user.id,
      payload: {
        newPassword: this.form.controls.newPassword.value.trim(),
      },
    });
  }

  get passwordMismatch(): boolean {
    const { newPassword, confirmPassword } = this.form.getRawValue();
    return !!confirmPassword && newPassword !== confirmPassword;
  }
}
