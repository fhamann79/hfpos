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
  @Input() downloadingSriRidePdfSaleId: number | null = null;

  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() signSriXml = new EventEmitter<number>();
  @Output() downloadSriXmlDraft = new EventEmitter<number>();
  @Output() downloadSriSignedXml = new EventEmitter<number>();
  @Output() downloadSriAuthorizedXml = new EventEmitter<number>();
  @Output() viewSriRide = new EventEmitter<number>();
  @Output() downloadSriRidePdf = new EventEmitter<number>();
  @Output() sendInvoiceEmail = new EventEmitter<number>();
  @Output() submitSriInvoice = new EventEmitter<number>();
  @Output() checkSriAuthorization = new EventEmitter<number>();
  @Output() viewSriAttempts = new EventEmitter<number>();
  @Output() viewInvoiceEmailDeliveries = new EventEmitter<number>();
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

  buyerName(sale: Sale): string {
    return this.trimToNull(sale.buyerNameSnapshot)
      ?? this.trimToNull(sale.customerName)
      ?? 'Consumidor final';
  }

  buyerIdentificationTypeLabel(sale: Sale): string {
    const type = this.trimToNull(sale.buyerIdentificationTypeSnapshot);

    switch (type) {
      case '04':
        return 'RUC';
      case '05':
        return 'Cédula';
      case '06':
        return 'Pasaporte';
      case '07':
        return 'Consumidor final';
      default:
        return '-';
    }
  }

  buyerIdentification(sale: Sale): string {
    return this.trimToNull(sale.buyerIdentificationSnapshot)
      ?? this.trimToNull(sale.customerIdentification)
      ?? '-';
  }

  buyerEmail(sale: Sale): string {
    return this.trimToNull(sale.buyerEmailSnapshot)
      ?? this.trimToNull(sale.customerEmail)
      ?? '-';
  }

  buyerAddress(sale: Sale): string {
    return this.trimToNull(sale.buyerAddressSnapshot) ?? '-';
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

  canDownloadSriRidePdf(sale: Sale): boolean {
    return this.canViewSriRide(sale);
  }

  canSendInvoiceEmail(sale: Sale): boolean {
    return this.canSubmitSriDocuments
      && sale.documentType === SaleDocumentType.Invoice
      && this.isAuthorized(sale)
      && !sale.isVoided;
  }

  canViewSriAttempts(sale: Sale): boolean {
    return sale.documentType === SaleDocumentType.Invoice;
  }

  canViewInvoiceEmailDeliveries(sale: Sale): boolean {
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

  isDownloadingSriRidePdf(sale: Sale): boolean {
    return this.downloadingSriRidePdfSaleId === sale.id;
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }

  private trimToNull(value: string | null | undefined): string | null {
    const trimmed = value?.trim();

    return trimmed ? trimmed : null;
  }
}
