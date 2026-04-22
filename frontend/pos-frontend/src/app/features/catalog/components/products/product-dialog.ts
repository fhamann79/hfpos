import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { Category } from '../../models/category.model';
import { CreateProductRequest, Product, UpdateProductRequest } from '../../models/product.model';
import { CategoryService } from '../../services/category.service';

export type ProductDialogSubmit =
  | { mode: 'create'; payload: CreateProductRequest }
  | { mode: 'edit'; id: number; payload: UpdateProductRequest };

@Component({
  selector: 'app-product-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    InputNumberModule,
    CheckboxModule,
    ButtonModule,
  ],
  templateUrl: './product-dialog.html',
  styleUrl: './product-dialog.scss',
})
export class ProductDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly categoryService = inject(CategoryService);

  @Input({ required: true }) visible = false;
  @Input() product: Product | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<ProductDialogSubmit>();

  readonly categories = signal<Category[]>([]);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    categoryId: [0, [Validators.required, Validators.min(1)]],
    barcode: [''],
    internalCode: [''],
    price: [0, [Validators.required, Validators.min(0)]],
    isActive: [true],
  });

  get isEditMode() {
    return !!this.product;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] && this.visible) {
      this.loadCategories();
      this.syncForm();
    }

    if (changes['product'] && this.visible) {
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

    if (this.product) {
      this.submitForm.emit({
        mode: 'edit',
        id: this.product.id,
        payload: {
          name: values.name.trim(),
          categoryId: values.categoryId,
          barcode: this.normalizeOptionalIdentifier(values.barcode),
          internalCode: this.normalizeOptionalIdentifier(values.internalCode),
          price: values.price,
          isActive: values.isActive,
        },
      });
      return;
    }

    this.submitForm.emit({
      mode: 'create',
      payload: {
        name: values.name.trim(),
        categoryId: values.categoryId,
        barcode: this.normalizeOptionalIdentifier(values.barcode),
        internalCode: this.normalizeOptionalIdentifier(values.internalCode),
        price: values.price,
      },
    });
  }

  private loadCategories(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories.filter((category) => category.isActive)),
      error: () => this.categories.set([]),
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.product) {
      this.form.setValue({
        name: this.product.name,
        categoryId: this.product.categoryId,
        barcode: this.product.barcode ?? '',
        internalCode: this.product.internalCode ?? '',
        price: this.product.price,
        isActive: this.product.isActive,
      });
      return;
    }

    this.form.reset({
      name: '',
      categoryId: 0,
      barcode: '',
      internalCode: '',
      price: 0,
      isActive: true,
    });
  }

  private normalizeOptionalIdentifier(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
}
