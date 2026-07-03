import { CommonModule, CurrencyPipe } from '@angular/common';
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
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import {
  sriAuthorizationStatusLabel,
  sriAuthorizationStatusSeverity,
  sriReceptionStatusLabel,
  sriReceptionStatusSeverity,
} from '../../models/sri-submission-attempt.model';

@Component({
  selector: 'app-recent-sales-panel',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, ButtonModule, MessageModule, TagModule],
  templateUrl: './recent-sales-panel.html',
  styleUrl: './recent-sales-panel.scss',
})
export class RecentSalesPanel {
  @Input({ required: true }) sales: SaleListItem[] = [];
  @Input({ required: true }) loading = false;
  @Input() errorMessage = '';
  @Input() canVoid = false;
  @Input() companyTimeZoneId = 'America/Guayaquil';

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

  customerLabel(sale: SaleListItem): string {
    return this.trimToNull(sale.customerName) ?? 'Consumidor final';
  }

  customerIdentification(sale: SaleListItem): string | null {
    return this.trimToNull(sale.customerIdentification);
  }

  customerEmail(sale: SaleListItem): string | null {
    return this.trimToNull(sale.customerEmail);
  }

  saleCreatedAtLabel(sale: SaleListItem): string {
    return formatBusinessDateTime(sale.createdAt, this.companyTimeZoneId) || '-';
  }

  canVoidSale(sale: SaleListItem): boolean {
    if (sale.isVoided) {
      return false;
    }

    if (!this.isInvoice(sale)) {
      return true;
    }

    if (sale.documentStatus === SaleDocumentStatus.Rejected) {
      return true;
    }

    if (
      sale.documentStatus === SaleDocumentStatus.Authorized
      || sale.documentStatus === SaleDocumentStatus.PendingAuthorization
    ) {
      return false;
    }

    return sale.documentStatus === SaleDocumentStatus.Draft && !this.invoiceHasSriProcess(sale);
  }

  invoiceHasSriProcess(sale: SaleListItem): boolean {
    return this.hasValue(sale.sriSubmittedAt)
      || this.hasValue(sale.sriReceptionStatus)
      || this.hasValue(sale.sriAuthorizationStatus)
      || this.hasValue(sale.sriLastCheckedAt);
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

  private hasValue(value: string | null | undefined): boolean {
    return typeof value === 'string' && value.trim().length > 0;
  }

  private trimToNull(value: string | null | undefined): string | null {
    const trimmed = value?.trim();

    return trimmed ? trimmed : null;
  }
}
