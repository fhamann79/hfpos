import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';

@Component({
  selector: 'app-void-sale-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, DialogModule, TextareaModule, ButtonModule],
  templateUrl: './void-sale-dialog.html',
  styleUrl: './void-sale-dialog.scss',
})
export class VoidSaleDialog {
  @Input({ required: true }) visible = false;
  @Input({ required: true }) loading = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() confirmVoid = new EventEmitter<string>();

  reason = '';

  onVisibleChange(value: boolean): void {
    this.visibleChange.emit(value);
    if (!value) {
      this.reason = '';
    }
  }
}
