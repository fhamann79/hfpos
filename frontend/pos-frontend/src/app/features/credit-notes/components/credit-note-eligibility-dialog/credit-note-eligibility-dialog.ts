import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import {
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
} from '../../../pos-workstation/models/sale-document.model';
import { CreditNoteEligibility } from '../../models/credit-note-eligibility.model';
import { CreateCreditNoteDraftRequest, CreditNoteListItem } from '../../models/credit-note.model';

@Component({
  selector: 'app-credit-note-eligibility-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    MessageModule,
    TableModule,
    TagModule,
    TextareaModule,
  ],
  templateUrl: './credit-note-eligibility-dialog.html',
  styleUrl: './credit-note-eligibility-dialog.scss',
})
export class CreditNoteEligibilityDialog implements OnChanges {
  @Input({ required: true }) visible = false;
  @Input() loading = false;
  @Input() creating = false;
  @Input() errorMessage = '';
  @Input() eligibility: CreditNoteEligibility | null = null;
  @Input() creditNotes: CreditNoteListItem[] = [];
  @Input() historyLoading = false;
  @Input() historyErrorMessage = '';
  @Input() cancellingCreditNoteId: number | null = null;
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();
  @Output() refreshHistory = new EventEmitter<void>();
  @Output() createDraft = new EventEmitter<CreateCreditNoteDraftRequest>();
  @Output() requestCancelDraft = new EventEmitter<CreditNoteListItem>();
  @Output() viewCreditNoteDetail = new EventEmitter<CreditNoteListItem>();

  readonly form = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(300)],
    }),
    notes: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });

  private readonly quantityControls = new Map<number, FormControl<number | null>>();

  ngOnChanges(changes: SimpleChanges): void {
    if (
      changes['eligibility']
      || (changes['visible']?.currentValue === true && changes['visible']?.previousValue !== true)
    ) {
      this.rebuildForm();
      return;
    }

    if (changes['creating'] || changes['cancellingCreditNoteId']) {
      this.syncCreatingState();
    }
  }

  emissionDateLabel(): string {
    return formatBusinessDateTime(
      this.eligibility?.originalSaleDocumentIssuedAt,
      this.companyTimeZoneId
    ) || '-';
  }

  identificationTypeLabel(type: string | null): string {
    switch (type?.trim()) {
      case '04':
        return 'RUC';
      case '05':
        return 'Cédula';
      case '06':
        return 'Pasaporte';
      case '07':
        return 'Consumidor final';
      default:
        return type?.trim() || '-';
    }
  }

  historyDateLabel(value: string | null): string {
    return formatBusinessDateTime(value, this.companyTimeZoneId) || '-';
  }

  documentStatusLabel(note: CreditNoteListItem): string {
    return saleDocumentStatusLabel(note.documentStatus);
  }

  documentStatusSeverity(note: CreditNoteListItem) {
    return saleDocumentStatusSeverity(note.documentStatus);
  }

  isBusy(): boolean {
    return this.creating || this.cancellingCreditNoteId !== null;
  }

  quantityControl(saleItemId: number): FormControl<number | null> {
    const current = this.quantityControls.get(saleItemId);
    if (current) {
      return current;
    }

    const availableQuantity = this.eligibility?.items
      .find((item) => item.saleItemId === saleItemId)?.availableQuantity ?? 0;
    const control = this.buildQuantityControl(availableQuantity);
    this.quantityControls.set(saleItemId, control);
    return control;
  }

  canCreateDraft(): boolean {
    const eligibility = this.eligibility;
    if (
      !eligibility?.isEligible
      || this.loading
      || this.isBusy()
      || this.form.invalid
      || !this.form.controls.reason.value.trim()
    ) {
      return false;
    }

    const quantities = eligibility.items.map((item) => ({
      available: item.availableQuantity,
      requested: this.quantityValue(item.saleItemId),
    }));

    return quantities.some((item) => item.requested > 0)
      && quantities.every((item) =>
        Number.isFinite(item.requested)
        && item.requested >= 0
        && item.requested <= item.available
      );
  }

  submitDraft(): void {
    this.form.markAllAsTouched();
    this.quantityControls.forEach((control) => control.markAsTouched());

    if (!this.canCreateDraft() || !this.eligibility) {
      return;
    }

    const items = this.eligibility.items
      .map((item) => ({
        saleItemId: item.saleItemId,
        quantity: this.quantityValue(item.saleItemId),
      }))
      .filter((item) => item.quantity > 0);

    const notes = this.form.controls.notes.value.trim();
    this.createDraft.emit({
      originalSaleId: this.eligibility.originalSaleId,
      reason: this.form.controls.reason.value.trim(),
      notes: notes || null,
      items,
    });
  }

  requestVisibleChange(visible: boolean): void {
    if (!visible && this.isBusy()) {
      return;
    }

    this.visibleChange.emit(visible);
  }

  refreshAll(): void {
    if (this.isBusy()) {
      return;
    }

    this.refresh.emit();
    this.refreshHistory.emit();
  }

  cancelDraft(note: CreditNoteListItem): void {
    if (!note.canCancelDraft || this.isBusy()) {
      return;
    }

    this.requestCancelDraft.emit(note);
  }

  viewDetail(note: CreditNoteListItem): void {
    if (this.isBusy()) {
      return;
    }

    this.viewCreditNoteDetail.emit(note);
  }

  private rebuildForm(): void {
    this.form.reset({ reason: '', notes: '' });
    this.quantityControls.clear();

    for (const item of this.eligibility?.items ?? []) {
      this.quantityControls.set(
        item.saleItemId,
        this.buildQuantityControl(item.availableQuantity)
      );
    }

    this.syncCreatingState();
  }

  private buildQuantityControl(availableQuantity: number): FormControl<number | null> {
    return new FormControl<number | null>(0, [
      Validators.min(0),
      Validators.max(availableQuantity),
    ]);
  }

  private quantityValue(saleItemId: number): number {
    const value = Number(this.quantityControl(saleItemId).value);
    return Number.isFinite(value) ? value : 0;
  }

  private syncCreatingState(): void {
    const action = this.isBusy() ? 'disable' : 'enable';
    this.form[action]({ emitEvent: false });
    this.quantityControls.forEach((control) => control[action]({ emitEvent: false }));
  }
}
