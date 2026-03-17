import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { CreateRoleRequest, Role, UpdateRoleRequest } from '../../models/role.model';

export type RoleDialogSubmit =
  | { mode: 'create'; payload: CreateRoleRequest }
  | { mode: 'edit'; id: number; payload: UpdateRoleRequest };

@Component({
  selector: 'app-role-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, CheckboxModule, ButtonModule],
  templateUrl: './role-dialog.html',
  styleUrl: './role-dialog.scss',
})
export class RoleDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() role: Role | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<RoleDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required]],
    name: ['', [Validators.required]],
    isActive: [true],
  });

  get isEditMode(): boolean {
    return !!this.role;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.syncForm();
    }

    if (changes['role'] && this.visible) {
      this.syncForm();
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();

    if (this.role) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.role.id,
        payload: {
          name: values.name.trim(),
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        code: values.code.trim(),
        name: values.name.trim(),
      },
    });
  }

  private syncForm(): void {
    if (this.role) {
      this.form.reset({
        code: this.role.code,
        name: this.role.name,
        isActive: this.role.isActive,
      });
      this.form.controls.code.disable();
      return;
    }

    this.form.controls.code.enable();
    this.form.reset({
      code: '',
      name: '',
      isActive: true,
    });
  }
}
