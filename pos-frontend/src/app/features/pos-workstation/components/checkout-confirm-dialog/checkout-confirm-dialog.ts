import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-checkout-confirm-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DialogModule, ButtonModule],
  templateUrl: './checkout-confirm-dialog.html',
  styleUrl: './checkout-confirm-dialog.scss',
})
export class CheckoutConfirmDialog {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) total = 0;
  @Input({ required: true }) itemCount = 0;
  @Input() notes = '';
  @Input() loading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirm = new EventEmitter<void>();

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.visibleChange.emit(false);
      event.preventDefault();
      return;
    }

    if (event.key === 'Enter' && !this.loading) {
      this.confirm.emit();
      event.preventDefault();
    }
  }
}
