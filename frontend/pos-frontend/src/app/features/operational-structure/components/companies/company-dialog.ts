import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { Company, CreateCompanyRequest, UpdateCompanyRequest } from '../../models/company.model';

export type CompanyDialogSubmit =
  | { mode: 'create'; payload: CreateCompanyRequest }
  | { mode: 'edit'; id: number; payload: UpdateCompanyRequest };

@Component({
  selector: 'app-company-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, CheckboxModule, ButtonModule],
  templateUrl: './company-dialog.html',
  styleUrl: './company-dialog.scss',
})
export class CompanyDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() company: Company | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<CompanyDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2)]],
    isActive: [true],
  });

  get isEditMode() {
    return !!this.company;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['company'] || changes['visible']) {
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

    if (this.isEditMode && this.company) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.company.id,
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
        name: values.name.trim(),
      },
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.company) {
      this.form.setValue({
        name: this.company.name,
        isActive: this.company.isActive,
      });
      return;
    }

    this.form.reset({ name: '', isActive: true });
  }
}
