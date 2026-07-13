import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { formatBusinessDateTime } from '../../../../core/utils/business-date-format';
import {
  DocumentTagSeverity,
  SaleDocumentStatus,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
} from '../../../pos-workstation/models/sale-document.model';
import { CreditNote } from '../../models/credit-note.model';

@Component({
  selector: 'app-credit-note-detail-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    DialogModule,
    MessageModule,
    TableModule,
    TagModule,
  ],
  templateUrl: './credit-note-detail-dialog.html',
  styleUrl: './credit-note-detail-dialog.scss',
})
export class CreditNoteDetailDialog {
  @Input({ required: true }) visible = false;
  @Input() loading = false;
  @Input() errorMessage = '';
  @Input() creditNote: CreditNote | null = null;
  @Input() companyTimeZoneId = 'America/Guayaquil';

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();

  dateLabel(value: string | null): string {
    return formatBusinessDateTime(value, this.companyTimeZoneId) || '-';
  }

  documentStatusLabel(creditNote: CreditNote): string {
    return saleDocumentStatusLabel(creditNote.documentStatus);
  }

  documentStatusSeverity(creditNote: CreditNote): DocumentTagSeverity {
    return saleDocumentStatusSeverity(creditNote.documentStatus);
  }

  identificationTypeLabel(type: string | null): string {
    switch (type?.trim()) {
      case '04':
        return 'RUC';
      case '05':
        return 'Cédula';
      case '06':
        return 'Pasaporte';
      case '07':
        return 'Consumidor final';
      default:
        return type?.trim() || '-';
    }
  }

  vatRateLabel(rate: number): string {
    return `${new Intl.NumberFormat('es-EC', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    }).format((Number(rate) || 0) * 100)}%`;
  }

  isCancelled(creditNote: CreditNote): boolean {
    return creditNote.documentStatus === SaleDocumentStatus.Cancelled
      || creditNote.voidedAt !== null;
  }
}
