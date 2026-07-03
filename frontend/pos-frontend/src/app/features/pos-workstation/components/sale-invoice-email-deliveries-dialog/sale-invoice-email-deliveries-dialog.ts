import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import {
  SaleInvoiceEmailDelivery,
  saleInvoiceEmailDeliveryStatusLabel,
  saleInvoiceEmailDeliveryStatusSeverity,
} from '../../models/sale-invoice-email.model';
import { Sale } from '../../models/sale.model';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';

@Component({
  selector: 'app-sale-invoice-email-deliveries-dialog',
  standalone: true,
  imports: [CommonModule, DialogModule, ButtonModule, MessageModule, TableModule, TagModule],
  templateUrl: './sale-invoice-email-deliveries-dialog.html',
  styleUrl: './sale-invoice-email-deliveries-dialog.scss',
})
export class SaleInvoiceEmailDeliveriesDialog {
  @Input({ required: true }) visible = false;
  @Input() sale: Sale | null = null;
  @Input() deliveries: SaleInvoiceEmailDelivery[] = [];
  @Input() loading = false;
  @Input() errorMessage = '';
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();

  statusLabel(delivery: SaleInvoiceEmailDelivery): string {
    return saleInvoiceEmailDeliveryStatusLabel(delivery.status);
  }

  statusSeverity(delivery: SaleInvoiceEmailDelivery) {
    return saleInvoiceEmailDeliveryStatusSeverity(delivery.status);
  }

  deliveryDateLabel(delivery: SaleInvoiceEmailDelivery): string {
    return this.formatTechnicalInstant(delivery.sentAt || delivery.createdAt);
  }

  deliveryCreatedAtLabel(delivery: SaleInvoiceEmailDelivery): string {
    return this.formatTechnicalInstant(delivery.createdAt);
  }

  errorDetail(delivery: SaleInvoiceEmailDelivery): string {
    if (delivery.errorCode && delivery.errorMessage) {
      return `${delivery.errorCode}: ${delivery.errorMessage}`;
    }

    return delivery.errorMessage || delivery.errorCode || '-';
  }

  private formatTechnicalInstant(value: string | null | undefined): string {
    return formatBusinessDateTime(value, this.companyTimeZoneId) || '-';
  }
}
