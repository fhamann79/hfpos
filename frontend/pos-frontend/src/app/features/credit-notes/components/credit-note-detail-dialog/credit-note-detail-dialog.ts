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
  @Input() canPrepareSriDraft = false;
  @Input() preparingSriDraft = false;
  @Input() downloadingSriXmlDraft = false;
  @Input() signingSriXml = false;
  @Input() downloadingSriSignedXml = false;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() refresh = new EventEmitter<void>();
  @Output() prepareSriDraft = new EventEmitter<number>();
  @Output() downloadSriXmlDraft = new EventEmitter<number>();
  @Output() signSriXml = new EventEmitter<number>();
  @Output() downloadSriSignedXml = new EventEmitter<number>();

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

  environmentLabel(environment: number | null): string {
    switch (environment) {
      case 1:
        return 'Pruebas';
      case 2:
        return 'Producción';
      default:
        return 'Pendiente';
    }
  }

  canShowPrepareSriDraft(creditNote: CreditNote): boolean {
    return this.canPrepareSriDraft
      && creditNote.documentStatus === SaleDocumentStatus.Draft
      && creditNote.voidedAt === null
      && creditNote.accessKey === null
      && !creditNote.hasSriXmlDraft;
  }

  canShowSignSriXml(creditNote: CreditNote): boolean {
    return this.canPrepareSriDraft
      && creditNote.documentStatus === SaleDocumentStatus.Draft
      && creditNote.voidedAt === null
      && !!creditNote.accessKey?.trim()
      && creditNote.hasSriXmlDraft
      && !creditNote.hasSriSignedXml;
  }

  isBusy(): boolean {
    return this.preparingSriDraft
      || this.downloadingSriXmlDraft
      || this.signingSriXml
      || this.downloadingSriSignedXml;
  }

  requestVisibleChange(visible: boolean): void {
    if (!visible && this.isBusy()) {
      return;
    }

    this.visibleChange.emit(visible);
  }

  requestPrepareSriDraft(creditNote: CreditNote): void {
    if (!this.canShowPrepareSriDraft(creditNote) || this.isBusy()) {
      return;
    }

    this.prepareSriDraft.emit(creditNote.id);
  }

  requestDownloadSriXmlDraft(creditNote: CreditNote): void {
    if (!creditNote.hasSriXmlDraft || this.isBusy()) {
      return;
    }

    this.downloadSriXmlDraft.emit(creditNote.id);
  }

  requestSignSriXml(creditNote: CreditNote): void {
    if (!this.canShowSignSriXml(creditNote) || this.isBusy()) {
      return;
    }

    this.signSriXml.emit(creditNote.id);
  }

  requestDownloadSriSignedXml(creditNote: CreditNote): void {
    if (!creditNote.hasSriSignedXml || this.isBusy()) {
      return;
    }

    this.downloadSriSignedXml.emit(creditNote.id);
  }
}
