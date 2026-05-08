import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import { SaleItem } from '../../models/sale-item.model';
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

  getVatLabel(item: SaleItem): string {
    return getVatCategoryOption(item.vatCategory).shortLabel;
  }
}
