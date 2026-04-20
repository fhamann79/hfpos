import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import {
  CreateEmissionPointRequest,
  EmissionPoint,
  UpdateEmissionPointRequest,
} from '../../models/emission-point.model';

export type EmissionPointDialogSubmit =
  | { mode: 'create'; payload: CreateEmissionPointRequest }
  | { mode: 'edit'; id: number; payload: UpdateEmissionPointRequest };

@Component({
  selector: 'app-emission-point-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, CheckboxModule, ButtonModule],
  templateUrl: './emission-point-dialog.html',
  styleUrl: './emission-point-dialog.scss',
})
export class EmissionPointDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() emissionPoint: EmissionPoint | null = null;
  @Input({ required: true }) establishmentId: number | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<EmissionPointDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.minLength(1)]],
    name: ['', [Validators.required, Validators.minLength(2)]],
    isActive: [true],
  });

  get isEditMode() {
    return !!this.emissionPoint;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['emissionPoint'] || changes['visible']) {
      this.syncForm();
    }
  }

  hide(): void {
    this.visibleChange.emit(false);
  }

  save(): void {
    if (this.form.invalid || !this.establishmentId) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();

    if (this.isEditMode && this.emissionPoint) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.emissionPoint.id,
        payload: {
          establishmentId: this.establishmentId,
          code: values.code.trim(),
          name: values.name.trim(),
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        establishmentId: this.establishmentId,
        code: values.code.trim(),
        name: values.name.trim(),
      },
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.emissionPoint) {
      this.form.setValue({
        code: this.emissionPoint.code,
        name: this.emissionPoint.name,
        isActive: this.emissionPoint.isActive,
      });
      return;
    }

    this.form.reset({ code: '', name: '', isActive: true });
  }
}
