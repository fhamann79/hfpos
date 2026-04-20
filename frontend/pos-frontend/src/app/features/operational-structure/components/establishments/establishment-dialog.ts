import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import {
  CreateEstablishmentRequest,
  Establishment,
  UpdateEstablishmentRequest,
} from '../../models/establishment.model';

export type EstablishmentDialogSubmit =
  | { mode: 'create'; payload: CreateEstablishmentRequest }
  | { mode: 'edit'; id: number; payload: UpdateEstablishmentRequest };

@Component({
  selector: 'app-establishment-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, CheckboxModule, ButtonModule],
  templateUrl: './establishment-dialog.html',
  styleUrl: './establishment-dialog.scss',
})
export class EstablishmentDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() establishment: Establishment | null = null;
  @Input({ required: true }) companyId: number | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<EstablishmentDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2)]],
    isActive: [true],
  });

  get isEditMode() {
    return !!this.establishment;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['establishment'] || changes['visible']) {
      this.syncForm();
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  save(): void {
    if (this.form.invalid || !this.companyId) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();

    if (this.isEditMode && this.establishment) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.establishment.id,
        payload: {
          companyId: this.companyId,
          name: values.name.trim(),
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        companyId: this.companyId,
        name: values.name.trim(),
      },
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.establishment) {
      this.form.setValue({
        name: this.establishment.name,
        isActive: this.establishment.isActive,
      });
      return;
    }

    this.form.reset({ name: '', isActive: true });
  }
}
