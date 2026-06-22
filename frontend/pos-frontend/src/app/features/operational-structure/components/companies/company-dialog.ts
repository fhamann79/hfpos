import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { Company, CreateCompanyRequest, UpdateCompanyRequest } from '../../models/company.model';

export type CompanyDialogSubmit =
  | { mode: 'create'; payload: CreateCompanyRequest }
  | { mode: 'edit'; id: number; payload: UpdateCompanyRequest };

@Component({
  selector: 'app-company-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, SelectModule, CheckboxModule, ButtonModule],
  templateUrl: './company-dialog.html',
  styleUrl: './company-dialog.scss',
})
export class CompanyDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  readonly timeZoneOptions = [
    { label: 'America/Guayaquil - Ecuador', value: 'America/Guayaquil' },
    { label: 'America/Bogota - Colombia', value: 'America/Bogota' },
    { label: 'America/Lima - Peru', value: 'America/Lima' },
    { label: 'America/Mexico_City - Mexico', value: 'America/Mexico_City' },
    { label: 'America/New_York - EE.UU. Este', value: 'America/New_York' },
    { label: 'America/Los_Angeles - EE.UU. Pacifico', value: 'America/Los_Angeles' },
    { label: 'Europe/Madrid - Espana', value: 'Europe/Madrid' },
    { label: 'Etc/UTC - UTC', value: 'Etc/UTC' },
  ];

  @Input({ required: true }) visible = false;
  @Input() company: Company | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<CompanyDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2)]],
    timeZoneId: ['America/Guayaquil', [Validators.required]],
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
          timeZoneId: values.timeZoneId,
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        name: values.name.trim(),
        timeZoneId: values.timeZoneId,
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
        timeZoneId: this.company.timeZoneId,
        isActive: this.company.isActive,
      });
      return;
    }

    this.form.reset({ name: '', timeZoneId: 'America/Guayaquil', isActive: true });
  }
}
