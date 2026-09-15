import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TextareaModule } from 'primeng/textarea';
import {
  CreditNote,
  ReturnCreditNoteInventoryRequest,
} from '../../models/credit-note.model';

@Component({
  selector: 'app-credit-note-inventory-return-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    MessageModule,
    TableModule,
    TextareaModule,
  ],
  templateUrl: './credit-note-inventory-return-dialog.html',
  styleUrl: './credit-note-inventory-return-dialog.scss',
})
export class CreditNoteInventoryReturnDialog implements OnChanges {
  @Input({ required: true }) visible = false;
  @Input() creditNote: CreditNote | null = null;
  @Input() loading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirmReturn = new EventEmitter<ReturnCreditNoteInventoryRequest>();

  readonly form = new FormGroup({
    notes: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (
      changes['creditNote']
      || (changes['visible']?.currentValue === true
        && changes['visible']?.previousValue !== true)
    ) {
      this.form.reset({ notes: '' });
    }

    if (changes['loading'] || changes['creditNote'] || changes['visible']) {
      this.syncLoadingState();
    }
  }

  requestVisibleChange(visible: boolean): void {
    if (!visible && this.loading) {
      return;
    }

    this.visibleChange.emit(visible);
  }

  submit(): void {
    this.form.markAllAsTouched();

    if (this.loading || !this.creditNote || this.form.invalid) {
      return;
    }

    const notes = this.form.controls.notes.value.trim();
    this.confirmReturn.emit({ notes: notes || null });
  }

  private syncLoadingState(): void {
    if (this.loading) {
      this.form.disable({ emitEvent: false });
    } else {
      this.form.enable({ emitEvent: false });
    }
  }
}
