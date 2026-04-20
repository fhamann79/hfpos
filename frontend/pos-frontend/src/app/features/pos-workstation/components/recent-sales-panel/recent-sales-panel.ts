import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { SaleListItem } from '../../models/sale-list-item.model';

@Component({
  selector: 'app-recent-sales-panel',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, ButtonModule, MessageModule],
  templateUrl: './recent-sales-panel.html',
  styleUrl: './recent-sales-panel.scss',
})
export class RecentSalesPanel {
  @Input({ required: true }) sales: SaleListItem[] = [];
  @Input({ required: true }) loading = false;
  @Input() errorMessage = '';
  @Input() canVoid = false;

  @Output() refresh = new EventEmitter<void>();
  @Output() viewDetail = new EventEmitter<number>();
  @Output() startVoid = new EventEmitter<SaleListItem>();
}
