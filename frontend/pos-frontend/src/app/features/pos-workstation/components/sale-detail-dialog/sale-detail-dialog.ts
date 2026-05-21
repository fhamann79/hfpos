import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TagModule } from 'primeng/tag';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import {
  DocumentTagSeverity,
  SaleDocumentType,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  saleDocumentTypeLabel,
  sriEnvironmentLabel,
  sriSignatureStatusLabel,
  sriSignatureStatusSeverity,
} from '../../models/sale-document.model';
import { SaleItem } from '../../models/sale-item.model';
import { Sale } from '../../models/sale.model';

@Component({
  selector: 'app-sale-detail-dialog',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, DatePipe, DialogModule, ButtonModule, TagModule],
  templateUrl: './sale-detail-dialog.html',
  styleUrl: './sale-detail-dialog.scss',
})
export class SaleDetailDialog {
  @Input({ required: true }) visible = false;
  @Input() sale: Sale | null = null;
  @Input() canSignSriDocuments = false;
  @Input() signingSaleId: number | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() signSriXml = new EventEmitter<number>();
  @Output() downloadSriXmlDraft = new EventEmitter<number>();
  @Output() downloadSriSignedXml = new EventEmitter<number>();

  getVatLabel(item: SaleItem): string {
    return getVatCategoryOption(item.vatCategory).shortLabel;
  }

  documentTypeLabel(sale: Sale): string {
    return saleDocumentTypeLabel(sale.documentType);
  }

  documentStatusLabel(sale: Sale): string {
    return saleDocumentStatusLabel(sale.documentStatus);
  }

  documentStatusSeverity(sale: Sale): DocumentTagSeverity {
    return saleDocumentStatusSeverity(sale.documentStatus);
  }

  sriEnvironmentLabel(sale: Sale): string {
    return sriEnvironmentLabel(sale.sriEnvironment);
  }

  signatureStatusLabel(sale: Sale): string {
    return sriSignatureStatusLabel(sale.hasSriSignedXml);
  }

  signatureStatusSeverity(sale: Sale): DocumentTagSeverity {
    return sriSignatureStatusSeverity(sale.hasSriSignedXml);
  }

  canSignSale(sale: Sale): boolean {
    return this.canSignSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && sale.hasSriXmlDraft
      && !sale.hasSriSignedXml
      && !sale.isVoided;
  }

  isSigning(sale: Sale): boolean {
    return this.signingSaleId === sale.id;
  }
}
