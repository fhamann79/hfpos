import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import {
  DocumentTagSeverity,
  SaleDocumentStatus,
  SaleDocumentType,
  saleDocumentTypeLabel,
  sriSignatureStatusLabel,
  sriSignatureStatusSeverity,
} from '../../models/sale-document.model';
import { SaleListItem } from '../../models/sale-list-item.model';
import {
  sriAuthorizationStatusLabel,
  sriAuthorizationStatusSeverity,
  sriReceptionStatusLabel,
  sriReceptionStatusSeverity,
} from '../../models/sri-submission-attempt.model';

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
    if (this.isAuthorized(sale)) {
      return sriSignatureStatusLabel(true);
    }

    return sriSignatureStatusLabel(sale.hasSriSignedXml, sale.sriSignatureStatusKnown);
  }

  signatureStatusSeverity(sale: SaleListItem): DocumentTagSeverity {
    if (this.isAuthorized(sale)) {
      return sriSignatureStatusSeverity(true);
    }

    return sriSignatureStatusSeverity(sale.hasSriSignedXml, sale.sriSignatureStatusKnown);
  }

  receptionStatusLabel(sale: SaleListItem): string {
    if (this.isAuthorized(sale)) {
      return sriReceptionStatusLabel('RECIBIDA');
    }

    return sriReceptionStatusLabel(sale.sriReceptionStatus);
  }

  receptionStatusSeverity(sale: SaleListItem): DocumentTagSeverity {
    if (this.isAuthorized(sale)) {
      return sriReceptionStatusSeverity('RECIBIDA');
    }

    return sriReceptionStatusSeverity(sale.sriReceptionStatus);
  }

  authorizationStatusLabel(sale: SaleListItem): string {
    if (this.isAuthorized(sale)) {
      return sriAuthorizationStatusLabel('AUTORIZADO');
    }

    return sriAuthorizationStatusLabel(sale.sriAuthorizationStatus);
  }

  authorizationStatusSeverity(sale: SaleListItem): DocumentTagSeverity {
    if (this.isAuthorized(sale)) {
      return sriAuthorizationStatusSeverity('AUTORIZADO');
    }

    return sriAuthorizationStatusSeverity(sale.sriAuthorizationStatus);
  }

  private isAuthorized(sale: SaleListItem): boolean {
    return sale.documentStatus === SaleDocumentStatus.Authorized
      || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO';
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }
}
