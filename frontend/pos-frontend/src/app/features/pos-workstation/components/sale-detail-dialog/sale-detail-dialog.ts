import { CommonModule, CurrencyPipe, DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TagModule } from 'primeng/tag';
import { getVatCategoryOption } from '../../../../core/utils/vat-category';
import {
  DocumentTagSeverity,
  SaleDocumentType,
  SaleDocumentStatus,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  saleDocumentTypeLabel,
  sriEnvironmentLabel,
  sriSignatureStatusLabel,
  sriSignatureStatusSeverity,
} from '../../models/sale-document.model';
import { SaleItem } from '../../models/sale-item.model';
import { Sale } from '../../models/sale.model';
import {
  sriAuthorizationStatusLabel,
  sriAuthorizationStatusSeverity,
  sriReceptionStatusLabel,
  sriReceptionStatusSeverity,
} from '../../models/sri-submission-attempt.model';

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
  @Input() canSubmitSriDocuments = false;
  @Input() signingSaleId: number | null = null;
  @Input() submittingSaleId: number | null = null;
  @Input() checkingAuthorizationSaleId: number | null = null;
  @Input() processingSriSaleId: number | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() signSriXml = new EventEmitter<number>();
  @Output() downloadSriXmlDraft = new EventEmitter<number>();
  @Output() downloadSriSignedXml = new EventEmitter<number>();
  @Output() downloadSriAuthorizedXml = new EventEmitter<number>();
  @Output() viewSriRide = new EventEmitter<number>();
  @Output() submitSriInvoice = new EventEmitter<number>();
  @Output() checkSriAuthorization = new EventEmitter<number>();
  @Output() viewSriAttempts = new EventEmitter<number>();
  @Output() processSriWorkflow = new EventEmitter<number>();

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

  isAuthorized(sale: Sale): boolean {
    return sale.documentStatus === SaleDocumentStatus.Authorized
      || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO';
  }

  receptionStatusLabel(sale: Sale): string {
    return sriReceptionStatusLabel(sale.sriReceptionStatus);
  }

  receptionStatusSeverity(sale: Sale): DocumentTagSeverity {
    return sriReceptionStatusSeverity(sale.sriReceptionStatus);
  }

  authorizationStatusLabel(sale: Sale): string {
    return sriAuthorizationStatusLabel(sale.sriAuthorizationStatus);
  }

  authorizationStatusSeverity(sale: Sale): DocumentTagSeverity {
    return sriAuthorizationStatusSeverity(sale.sriAuthorizationStatus);
  }

  canSignSale(sale: Sale): boolean {
    return this.canSignSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && sale.hasSriXmlDraft
      && !sale.hasSriSignedXml
      && !sale.isVoided
      && !this.isAuthorized(sale);
  }

  canSubmitSale(sale: Sale): boolean {
    return this.canSubmitSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && sale.hasSriSignedXml
      && !sale.isVoided
      && !this.isAuthorized(sale);
  }

  canCheckAuthorization(sale: Sale): boolean {
    return this.canSubmitSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && !!sale.accessKey
      && !sale.isVoided
      && !this.isAuthorized(sale);
  }

  canProcessSriWorkflow(sale: Sale): boolean {
    return this.canSignSriDocuments
      && this.canSubmitSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && sale.hasSriXmlDraft
      && !sale.isVoided
      && !this.isAuthorized(sale);
  }

  canDownloadAuthorizedXml(sale: Sale): boolean {
    return sale.documentType === SaleDocumentType.Invoice
      && this.isAuthorized(sale)
      && (
        !!sale.authorizationNumber
        || this.normalizeSriStatus(sale.sriAuthorizationStatus) === 'AUTORIZADO'
      );
  }

  canViewSriRide(sale: Sale): boolean {
    return sale.documentType === SaleDocumentType.Invoice
      && this.isAuthorized(sale);
  }

  canViewSriAttempts(sale: Sale): boolean {
    return sale.documentType === SaleDocumentType.Invoice;
  }

  hasManualSriActions(sale: Sale): boolean {
    return sale.hasSriXmlDraft
      || this.canSignSale(sale)
      || this.canSubmitSale(sale)
      || this.canCheckAuthorization(sale)
      || sale.hasSriSignedXml;
  }

  isSigning(sale: Sale): boolean {
    return this.signingSaleId === sale.id;
  }

  isSubmitting(sale: Sale): boolean {
    return this.submittingSaleId === sale.id;
  }

  isCheckingAuthorization(sale: Sale): boolean {
    return this.checkingAuthorizationSaleId === sale.id;
  }

  isProcessingSriWorkflow(sale: Sale): boolean {
    return this.processingSriSaleId === sale.id;
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }
}
