import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { Sale } from '../../models/sale.model';

@Component({
  selector: 'app-sale-detail-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, DialogModule],
  templateUrl: './sale-detail-dialog.html',
  styleUrl: './sale-detail-dialog.scss',
})
export class SaleDetailDialog {
  @Input({ required: true }) visible = false;
  @Input() sale: Sale | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
}
