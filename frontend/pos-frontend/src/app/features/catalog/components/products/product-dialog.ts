import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SelectModule } from 'primeng/select';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { FiscalProfileService } from '../../../fiscal-settings/services/fiscal-profile.service';
import { ProductFiscalClassification } from '../../../fiscal-settings/models/fiscal-profile.model';
import { DEFAULT_VAT_CATEGORY, VAT_CATEGORY_OPTIONS, normalizeVatCategory } from '../../../../core/utils/vat-category';
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
    ButtonModule,
  ],
  templateUrl: './product-dialog.html',
  styleUrl: './product-dialog.scss',
})
export class ProductDialog implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly categoryService = inject(CategoryService);
  private readonly fiscalService = inject(FiscalProfileService);
  readonly inventoryClassification = signal<ProductFiscalClassification | null>(null);

  @Input({ required: true }) visible = false;
  @Input() product: Product | null = null;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() submitForm = new EventEmitter<ProductDialogSubmit>();

  readonly categories = signal<Category[]>([]);
  readonly vatCategoryOptions = VAT_CATEGORY_OPTIONS;

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    tracksInventory: [true],
    categoryId: [0, [Validators.required, Validators.min(1)]],
    barcode: [''],
    internalCode: [''],
    price: [0, [Validators.required, Validators.min(0)]],
    cost: [0, [Validators.required, Validators.min(0)]],
    minimumStock: [3, [Validators.required, Validators.min(0)]],
    vatCategory: [DEFAULT_VAT_CATEGORY, [Validators.required]],
  });

  constructor() {
    this.form.controls.tracksInventory.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.syncInventoryControl();
    });
  }

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
          tracksInventory: values.tracksInventory,
          expectedClassificationRevision: this.inventoryClassification()?.revision,
          categoryId: values.categoryId,
          barcode: this.normalizeOptionalIdentifier(values.barcode),
          internalCode: this.normalizeOptionalIdentifier(values.internalCode),
          price: values.price,
          cost: values.cost,
          minimumStock: values.minimumStock,
          vatCategory: values.vatCategory,
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
        cost: values.cost,
        minimumStock: values.minimumStock,
        vatCategory: values.vatCategory,
      },
    });
  }

  private loadCategories(): void {
    this.categoryService.getAll().subscribe({
      next: (categories) => this.categories.set(categories.filter((category) => category.isActive || (!this.product?.isActive && category.id === this.product?.categoryId))),
      error: () => this.categories.set([]),
    });
  }

  private syncForm(): void {
    if (!this.visible) {
      return;
    }

    if (this.product) {
      const id = this.product.id;
      this.inventoryClassification.set(null);
      this.syncInventoryControl();
      this.fiscalService.product(id).subscribe({
        next: state => {
          if (this.visible && this.product?.id === id) {
            this.inventoryClassification.set(state.classification);
            this.syncInventoryControl();
          }
        },
        error: () => {
          if (this.product?.id === id) {
            this.inventoryClassification.set(null);
            this.syncInventoryControl();
          }
        },
      });
      this.form.setValue({
        name: this.product.name,
        tracksInventory: this.product.tracksInventory !== false,
        categoryId: this.product.categoryId,
        barcode: this.product.barcode ?? '',
        internalCode: this.product.internalCode ?? '',
        price: this.product.price,
        cost: this.product.cost,
        minimumStock: this.product.minimumStock,
        vatCategory: normalizeVatCategory(this.product.vatCategory),
      });
      return;
    }

    this.form.reset({
      name: '',
      tracksInventory: true,
      categoryId: 0,
      barcode: '',
      internalCode: '',
      price: 0,
      cost: 0,
      minimumStock: 3,
      vatCategory: DEFAULT_VAT_CATEGORY,
    });
  }

  private syncInventoryControl(): void {
    const control = this.form.controls.tracksInventory;
    const classification = this.inventoryClassification();
    if (control.value && !(classification?.confirmed && classification.isCurrent && classification.kind === 1)) {
      control.disable({ emitEvent: false });
    } else {
      control.enable({ emitEvent: false });
    }
  }

  private normalizeOptionalIdentifier(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
}
