import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import {
  DocumentTagSeverity,
  SaleDocumentType,
  saleDocumentTypeLabel,
  sriSignatureStatusLabel,
  sriSignatureStatusSeverity,
} from '../../models/sale-document.model';
import { SaleListItem } from '../../models/sale-list-item.model';

@Component({
  selector: 'app-recent-sales-panel',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, ButtonModule, MessageModule, TagModule],
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

  documentTypeLabel(sale: SaleListItem): string {
    return saleDocumentTypeLabel(sale.documentType);
  }

  documentTypeSeverity(sale: SaleListItem): DocumentTagSeverity {
    return sale.documentType === SaleDocumentType.Invoice ? 'info' : 'secondary';
  }

  isInvoice(sale: SaleListItem): boolean {
    return sale.documentType === SaleDocumentType.Invoice;
  }

  signatureStatusLabel(sale: SaleListItem): string {
    return sriSignatureStatusLabel(sale.hasSriSignedXml, sale.sriSignatureStatusKnown);
  }

  signatureStatusSeverity(sale: SaleListItem): DocumentTagSeverity {
    return sriSignatureStatusSeverity(sale.hasSriSignedXml, sale.sriSignatureStatusKnown);
  }
}
