import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TextareaModule } from 'primeng/textarea';
import { CreditNoteListItem } from '../../models/credit-note.model';

@Component({
  selector: 'app-cancel-credit-note-draft-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    MessageModule,
    TextareaModule,
  ],
  templateUrl: './cancel-credit-note-draft-dialog.html',
  styleUrl: './cancel-credit-note-draft-dialog.scss',
})
export class CancelCreditNoteDraftDialog implements OnChanges {
  @Input({ required: true }) visible = false;
  @Input() loading = false;
  @Input() creditNote: CreditNoteListItem | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirmCancel = new EventEmitter<string>();

  readonly form = new FormGroup({
    reason: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(300)],
    }),
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (
      changes['creditNote']
      || (changes['visible']?.currentValue === true && changes['visible']?.previousValue !== true)
    ) {
      this.form.reset({ reason: '' });
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
    const reason = this.form.controls.reason.value.trim();

    if (this.loading || !this.creditNote || this.form.invalid || !reason) {
      return;
    }

    this.confirmCancel.emit(reason);
  }

  private syncLoadingState(): void {
    if (this.loading) {
      this.form.disable({ emitEvent: false });
    } else {
      this.form.enable({ emitEvent: false });
    }
  }
}
