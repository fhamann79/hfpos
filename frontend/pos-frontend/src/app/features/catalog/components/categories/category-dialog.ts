import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { Category, CreateCategoryRequest, UpdateCategoryRequest } from '../../models/category.model';

export type CategoryDialogSubmit =
  | { mode: 'create'; payload: CreateCategoryRequest }
  | { mode: 'edit'; id: number; payload: UpdateCategoryRequest };

@Component({
  selector: 'app-category-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DialogModule, InputTextModule, CheckboxModule, ButtonModule],
  templateUrl: './category-dialog.html',
  styleUrl: './category-dialog.scss',
})
export class CategoryDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);

  @Input({ required: true }) visible = false;
  @Input() category: Category | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<CategoryDialogSubmit>();

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2)]],
    isActive: [true],
  });

  get isEditMode() {
    return !!this.category;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['category'] || changes['visible']) {
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

    if (this.isEditMode && this.category) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.category.id,
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

    if (this.category) {
      this.form.setValue({
        name: this.category.name,
        isActive: this.category.isActive,
      });
      return;
    }

    this.form.reset({
      name: '',
      isActive: true,
    });
  }
}
