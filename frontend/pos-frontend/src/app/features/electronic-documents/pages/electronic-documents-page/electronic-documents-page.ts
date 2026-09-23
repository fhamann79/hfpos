import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import { Observable } from 'rxjs';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { AuthStore } from '../../../../core/stores/auth.store';
import {
  formatBusinessDate,
  formatBusinessDateTime,
} from '../../../../core/utils/business-date-format';
import { SriRideDialog } from '../../../pos-workstation/components/sri-ride-dialog/sri-ride-dialog';
import {
  SaleInvoiceEmailDelivery,
  saleInvoiceEmailDeliveryStatusLabel,
  saleInvoiceEmailDeliveryStatusSeverity,
} from '../../../pos-workstation/models/sale-invoice-email.model';
import {
  DocumentTagSeverity,
  SaleDocumentStatus,
  saleDocumentStatusLabel,
  saleDocumentStatusSeverity,
  sriEnvironmentLabel,
} from '../../../pos-workstation/models/sale-document.model';
import { SriRide } from '../../../pos-workstation/models/sri-ride.model';
import {
  SriSubmissionAttempt,
  sriAuthorizationStatusLabel,
  sriAuthorizationStatusSeverity,
  sriReceptionStatusLabel,
  sriReceptionStatusSeverity,
  sriSubmissionAttemptStatusLabel,
  sriSubmissionAttemptStatusSeverity,
  sriSubmissionAttemptTypeLabel,
} from '../../../pos-workstation/models/sri-submission-attempt.model';
import {
  ElectronicDocumentDetail,
  ElectronicDocumentKind,
  ElectronicDocumentListItem,
  ElectronicDocumentQuery,
  ElectronicDocumentSortField,
  ElectronicDocumentSummary,
  electronicDocumentKindLabel,
} from '../../models/electronic-document.model';
import { ElectronicDocumentService } from '../../services/electronic-document.service';

interface SelectOption<T> {
  label: string;
  value: T;
}

const EMPTY_SUMMARY: ElectronicDocumentSummary = {
  totalDocuments: 0,
  invoiceCount: 0,
  creditNoteCount: 0,
  draftCount: 0,
  pendingAuthorizationCount: 0,
  authorizedCount: 0,
  rejectedCount: 0,
  cancelledCount: 0,
  withSriErrorCount: 0,
};

@Component({
  selector: 'app-electronic-documents-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CheckboxModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    TooltipModule,
    SriRideDialog,
  ],
  templateUrl: './electronic-documents-page.html',
  styleUrl: './electronic-documents-page.scss',
})
export class ElectronicDocumentsPage implements OnInit {
  private readonly documentService = inject(ElectronicDocumentService);
  private readonly permissionService = inject(PermissionService);
  private readonly authStore = inject(AuthStore);

  readonly documents = signal<ElectronicDocumentListItem[]>([]);
  readonly summary = signal<ElectronicDocumentSummary>(EMPTY_SUMMARY);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly currentPage = signal(1);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');

  readonly detailVisible = signal(false);
  readonly detailLoading = signal(false);
  readonly detailError = signal('');
  readonly selectedDocument = signal<ElectronicDocumentDetail | null>(null);
  readonly actionLoadingKey = signal('');

  readonly rideVisible = signal(false);
  readonly rideLoading = signal(false);
  readonly rideError = signal('');
  readonly ride = signal<SriRide | null>(null);

  readonly attemptsVisible = signal(false);
  readonly attemptsLoading = signal(false);
  readonly attemptsError = signal('');
  readonly attempts = signal<SriSubmissionAttempt[]>([]);

  readonly deliveriesVisible = signal(false);
  readonly deliveriesLoading = signal(false);
  readonly deliveriesError = signal('');
  readonly deliveries = signal<SaleInvoiceEmailDelivery[]>([]);

  readonly emailVisible = signal(false);
  readonly emailSending = signal(false);
  readonly emailError = signal('');
  emailTo = '';
  emailCc = '';
  emailSubject = '';
  emailMessage = '';
  emailSubmitted = false;

  from = '';
  to = '';
  search = '';
  kind: ElectronicDocumentKind | null = null;
  documentStatus: SaleDocumentStatus | null = null;
  onlyWithSriError = false;
  first = 0;
  rows = 15;
  sortField: ElectronicDocumentSortField = 'documentDate';
  sortOrder = -1;

  readonly companyTimeZoneId = computed(() => this.authStore.companyTimeZoneId());
  readonly ElectronicDocumentKind = ElectronicDocumentKind;
  readonly canSignDocuments = computed(() =>
    this.permissionService.hasPermission(PERMISSIONS.sriDocumentsSign)
  );
  readonly canSubmitDocuments = computed(() =>
    this.permissionService.hasPermission(PERMISSIONS.sriDocumentsSubmit)
  );
  readonly canReadCreditNoteArtifacts = computed(() =>
    this.permissionService.hasPermission(PERMISSIONS.posSalesVoid)
  );

  readonly kindOptions: SelectOption<ElectronicDocumentKind>[] = [
    { label: 'Factura', value: ElectronicDocumentKind.Invoice },
    { label: 'Nota de crédito', value: ElectronicDocumentKind.CreditNote },
  ];

  readonly documentStatusOptions: SelectOption<SaleDocumentStatus>[] = [
    { label: 'Borrador', value: SaleDocumentStatus.Draft },
    { label: 'Pendiente autorización', value: SaleDocumentStatus.PendingAuthorization },
    { label: 'Autorizado', value: SaleDocumentStatus.Authorized },
    { label: 'Rechazado', value: SaleDocumentStatus.Rejected },
    { label: 'Cancelado', value: SaleDocumentStatus.Cancelled },
  ];

  ngOnInit(): void {
    this.loadDocuments(1, this.rows);
  }

  loadDocuments(page = this.currentPage(), pageSize = this.rows): void {
    this.loading.set(true);
    this.errorMessage.set('');

    this.documentService.getDocuments(this.currentQuery(page, pageSize)).subscribe({
      next: (result) => {
        if (result.totalPages > 0 && result.page > result.totalPages) {
          this.loadDocuments(result.totalPages, result.pageSize);
          return;
        }

        this.documents.set(result.items);
        this.summary.set(result.summary ?? EMPTY_SUMMARY);
        this.totalItems.set(result.totalItems);
        this.totalPages.set(result.totalPages);
        this.rows = result.pageSize;
        this.first = result.totalItems === 0 ? 0 : (result.page - 1) * result.pageSize;
        this.currentPage.set(result.totalItems === 0 ? 1 : result.page);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        this.errorMessage.set(this.resolveError(error, 'No se pudo cargar el centro documental.'));
      },
    });
  }

  onLazyLoad(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.rows;
    const requestedSort = typeof event.sortField === 'string' && this.isSortField(event.sortField)
      ? event.sortField
      : this.sortField;
    const requestedOrder = event.sortOrder === 1 || event.sortOrder === -1
      ? event.sortOrder
      : this.sortOrder;
    const sortChanged = requestedSort !== this.sortField || requestedOrder !== this.sortOrder;

    this.sortField = requestedSort;
    this.sortOrder = requestedOrder;
    const requestedFirst = sortChanged ? 0 : (event.first ?? this.first);
    this.loadDocuments(Math.floor(requestedFirst / rows) + 1, rows);
  }

  applyFilters(): void {
    this.first = 0;
    this.currentPage.set(1);
    this.loadDocuments(1, this.rows);
  }

  clearFilters(): void {
    this.from = '';
    this.to = '';
    this.search = '';
    this.kind = null;
    this.documentStatus = null;
    this.onlyWithSriError = false;
    this.sortField = 'documentDate';
    this.sortOrder = -1;
    this.applyFilters();
  }

  openDetail(row: ElectronicDocumentListItem): void {
    this.detailVisible.set(true);
    this.detailLoading.set(true);
    this.detailError.set('');
    this.selectedDocument.set(null);

    this.documentService.getDetail(row.kind, row.id).subscribe({
      next: (detail) => {
        this.selectedDocument.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(this.resolveError(error, 'No se pudo cargar el detalle fiscal.'));
      },
    });
  }

  closeDetail(): void {
    if (this.actionLoadingKey()) {
      return;
    }
    this.detailVisible.set(false);
    this.selectedDocument.set(null);
    this.detailError.set('');
  }

  prepareSriDraft(document: ElectronicDocumentDetail): void {
    this.runMutation(document, 'prepare', () =>
      this.documentService.prepareSriDraft(document.kind, document.id)
    );
  }

  signSri(document: ElectronicDocumentDetail): void {
    this.runMutation(document, 'sign', () =>
      this.documentService.signSri(document.kind, document.id)
    );
  }

  submitSri(document: ElectronicDocumentDetail): void {
    this.runMutation(document, 'submit', () =>
      this.documentService.submitSri(document.kind, document.id)
    );
  }

  checkAuthorization(document: ElectronicDocumentDetail): void {
    this.runMutation(document, 'check', () =>
      this.documentService.checkSriAuthorization(document.kind, document.id)
    );
  }

  downloadDraft(document: ElectronicDocumentDetail): void {
    this.download(document, 'xml-draft', () =>
      this.documentService.getSriXmlDraft(document.kind, document.id)
    );
  }

  downloadSigned(document: ElectronicDocumentDetail): void {
    this.download(document, 'signed-xml', () =>
      this.documentService.getSriSignedXml(document.kind, document.id)
    );
  }

  downloadAuthorized(document: ElectronicDocumentDetail): void {
    this.download(document, 'authorized-xml', () =>
      this.documentService.getSriAuthorizedXml(document.kind, document.id)
    );
  }

  downloadRidePdf(document: ElectronicDocumentDetail): void {
    this.download(
      document,
      'ride',
      () => this.documentService.getSriRidePdf(document.kind, document.id),
      'pdf'
    );
  }

  viewRide(document: ElectronicDocumentDetail): void {
    if (this.actionLoadingKey()) {
      return;
    }

    this.rideVisible.set(true);
    this.rideLoading.set(true);
    this.rideError.set('');
    this.ride.set(null);
    this.actionLoadingKey.set(`${document.key}:ride`);
    this.documentService.getSriRide(document.kind, document.id).subscribe({
      next: (ride) => {
        this.ride.set(ride);
        this.rideLoading.set(false);
        this.actionLoadingKey.set('');
      },
      error: (error: HttpErrorResponse) => {
        this.rideLoading.set(false);
        this.actionLoadingKey.set('');
        this.rideError.set(this.resolveError(error, 'No se pudo cargar el RIDE.'));
      },
    });
  }

  openAttempts(document: ElectronicDocumentDetail): void {
    this.attemptsVisible.set(true);
    this.attemptsLoading.set(true);
    this.attemptsError.set('');
    this.attempts.set([]);
    this.documentService.getSriSubmissionAttempts(document.kind, document.id).subscribe({
      next: (attempts) => {
        this.attempts.set(attempts);
        this.attemptsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.attemptsLoading.set(false);
        this.attemptsError.set(this.resolveError(error, 'No se pudo cargar el historial SRI.'));
      },
    });
  }

  openDeliveries(document: ElectronicDocumentDetail): void {
    this.deliveriesVisible.set(true);
    this.deliveriesLoading.set(true);
    this.deliveriesError.set('');
    this.deliveries.set([]);
    this.documentService.getSriEmailDeliveries(document.kind, document.id).subscribe({
      next: (deliveries) => {
        this.deliveries.set(deliveries);
        this.deliveriesLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.deliveriesLoading.set(false);
        this.deliveriesError.set(this.resolveError(error, 'No se pudo cargar el historial de emails.'));
      },
    });
  }

  openEmail(document: ElectronicDocumentDetail): void {
    this.emailTo = document.buyerEmail?.trim() ?? '';
    this.emailCc = '';
    this.emailSubject = '';
    this.emailMessage = '';
    this.emailSubmitted = false;
    this.emailError.set('');
    this.emailVisible.set(true);
  }

  sendEmail(document: ElectronicDocumentDetail): void {
    this.emailSubmitted = true;
    if (!this.isValidEmail(this.emailTo) || (!!this.emailCc.trim() && !this.isValidEmail(this.emailCc))) {
      return;
    }

    this.emailSending.set(true);
    this.emailError.set('');
    this.documentService.sendSriEmail(document.kind, document.id, {
      toEmail: this.emailTo.trim(),
      ccEmail: this.trimToNull(this.emailCc),
      subject: this.trimToNull(this.emailSubject),
      message: this.trimToNull(this.emailMessage),
    }).subscribe({
      next: () => {
        this.emailSending.set(false);
        this.emailVisible.set(false);
        this.successMessage.set('Documento enviado por email correctamente.');
      },
      error: (error: HttpErrorResponse) => {
        this.emailSending.set(false);
        this.emailError.set(this.resolveError(error, 'No se pudo enviar el documento por email.'));
      },
    });
  }

  canPrepare(document: ElectronicDocumentDetail): boolean {
    return document.kind === ElectronicDocumentKind.CreditNote
      && this.canSignDocuments()
      && document.documentStatus === SaleDocumentStatus.Draft
      && !document.hasSriXmlDraft
      && !document.accessKey;
  }

  canSign(document: ElectronicDocumentDetail): boolean {
    return this.canSignDocuments()
      && document.documentStatus === SaleDocumentStatus.Draft
      && document.hasSriXmlDraft
      && !document.hasSriSignedXml;
  }

  canSubmit(document: ElectronicDocumentDetail): boolean {
    return this.canSubmitDocuments()
      && document.documentStatus === SaleDocumentStatus.Draft
      && document.hasSriSignedXml
      && !document.sriSubmittedAt
      && this.normalizeSriStatus(document.sriReceptionStatus) !== 'RECIBIDA';
  }

  canCheck(document: ElectronicDocumentDetail): boolean {
    return this.canSubmitDocuments()
      && document.documentStatus === SaleDocumentStatus.PendingAuthorization
      && !!document.accessKey
      && !!document.sriSubmittedAt
      && this.normalizeSriStatus(document.sriReceptionStatus) === 'RECIBIDA'
      && this.normalizeSriStatus(document.sriAuthorizationStatus) !== 'AUTORIZADO';
  }

  canReadArtifacts(document: ElectronicDocumentDetail): boolean {
    return document.kind === ElectronicDocumentKind.Invoice || this.canReadCreditNoteArtifacts();
  }

  canEmail(document: ElectronicDocumentDetail): boolean {
    return this.canSubmitDocuments() && this.isAuthorized(document);
  }

  isAuthorized(document: ElectronicDocumentListItem): boolean {
    return document.documentStatus === SaleDocumentStatus.Authorized
      || this.normalizeSriStatus(document.sriAuthorizationStatus) === 'AUTORIZADO';
  }

  isActionLoading(document: ElectronicDocumentDetail, action: string): boolean {
    return this.actionLoadingKey() === `${document.key}:${action}`;
  }

  kindLabel(kind: ElectronicDocumentKind): string {
    return electronicDocumentKindLabel(kind);
  }

  kindSeverity(kind: ElectronicDocumentKind): DocumentTagSeverity {
    return kind === ElectronicDocumentKind.Invoice ? 'info' : 'contrast';
  }

  statusLabel(status: SaleDocumentStatus): string {
    return saleDocumentStatusLabel(status);
  }

  statusSeverity(status: SaleDocumentStatus): DocumentTagSeverity {
    return saleDocumentStatusSeverity(status);
  }

  receptionLabel(status: string | null): string {
    return sriReceptionStatusLabel(status);
  }

  receptionSeverity(status: string | null): DocumentTagSeverity {
    return sriReceptionStatusSeverity(status);
  }

  authorizationLabel(status: string | null): string {
    return sriAuthorizationStatusLabel(status);
  }

  authorizationSeverity(status: string | null): DocumentTagSeverity {
    return sriAuthorizationStatusSeverity(status);
  }

  environmentLabel(value: number | null): string {
    return sriEnvironmentLabel(value);
  }

  attemptTypeLabel(attempt: SriSubmissionAttempt): string {
    return sriSubmissionAttemptTypeLabel(attempt.attemptType);
  }

  attemptStatusLabel(attempt: SriSubmissionAttempt): string {
    return sriSubmissionAttemptStatusLabel(attempt.status);
  }

  attemptStatusSeverity(attempt: SriSubmissionAttempt): DocumentTagSeverity {
    return sriSubmissionAttemptStatusSeverity(attempt.status);
  }

  attemptMessage(attempt: SriSubmissionAttempt): string {
    return attempt.sriMessage || attempt.errorMessage || attempt.sriAdditionalInfo || '-';
  }

  deliveryStatusLabel(delivery: SaleInvoiceEmailDelivery): string {
    return saleInvoiceEmailDeliveryStatusLabel(delivery.status);
  }

  deliveryStatusSeverity(delivery: SaleInvoiceEmailDelivery): DocumentTagSeverity {
    return saleInvoiceEmailDeliveryStatusSeverity(delivery.status);
  }

  formatDate(value: string | null | undefined): string {
    return formatBusinessDate(value, this.companyTimeZoneId()) || '-';
  }

  formatDateTime(value: string | null | undefined): string {
    return formatBusinessDateTime(value, this.companyTimeZoneId()) || '-';
  }

  emailToInvalid(): boolean {
    return this.emailSubmitted && !this.isValidEmail(this.emailTo);
  }

  emailCcInvalid(): boolean {
    return this.emailSubmitted && !!this.emailCc.trim() && !this.isValidEmail(this.emailCc);
  }

  private currentQuery(page: number, pageSize: number): ElectronicDocumentQuery {
    return {
      from: this.from || null,
      to: this.to || null,
      kind: this.kind,
      documentStatus: this.documentStatus,
      search: this.search || null,
      onlyWithSriError: this.onlyWithSriError,
      page,
      pageSize,
      sortField: this.sortField,
      sortOrder: this.sortOrder === 1 ? 'asc' : 'desc',
    };
  }

  private runMutation(
    document: ElectronicDocumentDetail,
    action: string,
    operation: () => Observable<unknown>
  ): void {
    if (this.actionLoadingKey()) {
      return;
    }

    this.actionLoadingKey.set(`${document.key}:${action}`);
    this.detailError.set('');
    this.successMessage.set('');
    operation().subscribe({
      next: () => {
        this.actionLoadingKey.set('');
        this.successMessage.set('Operación SRI completada.');
        this.reloadSelectedDocument(document);
        this.loadDocuments(this.currentPage(), this.rows);
      },
      error: (error: HttpErrorResponse) => {
        this.actionLoadingKey.set('');
        this.detailError.set(this.resolveError(error, 'No se pudo completar la operación SRI.'));
      },
    });
  }

  private reloadSelectedDocument(document: ElectronicDocumentDetail): void {
    this.detailLoading.set(true);
    this.documentService.getDetail(document.kind, document.id).subscribe({
      next: (detail) => {
        this.selectedDocument.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.detailLoading.set(false);
        this.detailError.set(this.resolveError(error, 'No se pudo actualizar el detalle fiscal.'));
      },
    });
  }

  private download(
    document: ElectronicDocumentDetail,
    suffix: string,
    operation: () => Observable<Blob>,
    extension = 'xml'
  ): void {
    if (this.actionLoadingKey()) {
      return;
    }

    this.actionLoadingKey.set(`${document.key}:${suffix}`);
    this.detailError.set('');
    operation().subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = window.document.createElement('a');
        const number = document.number?.replace(/[^a-zA-Z0-9-]/g, '-') || String(document.id);
        anchor.href = url;
        const kindSlug = document.kind === ElectronicDocumentKind.Invoice ? 'factura' : 'nota-credito';
        anchor.download = `${kindSlug}-${number}-${suffix}.${extension}`;
        anchor.style.display = 'none';
        window.document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
        this.actionLoadingKey.set('');
      },
      error: (error: HttpErrorResponse) => {
        this.actionLoadingKey.set('');
        this.detailError.set(this.resolveError(error, 'No se pudo descargar el archivo.'));
      },
    });
  }

  private resolveError(error: HttpErrorResponse, fallback: string): string {
    if (typeof error.error === 'object' && error.error !== null) {
      const payload = error.error as Record<string, unknown>;
      const code = payload['error'] ?? payload['code'];
      if (typeof code === 'string' && code.trim()) {
        return `${fallback} (${code})`;
      }
    }

    if (typeof error.error === 'string' && error.error.trim()) {
      return `${fallback} (${error.error.trim()})`;
    }

    return fallback;
  }

  private isSortField(value: string): value is ElectronicDocumentSortField {
    return [
      'documentDate',
      'number',
      'buyerName',
      'kind',
      'documentStatus',
      'total',
      'authorizedAt',
    ].includes(value);
  }

  private normalizeSriStatus(status: string | null | undefined): string {
    return status?.trim().toUpperCase() ?? '';
  }

  private isValidEmail(value: string): boolean {
    const normalized = value.trim();
    return normalized.length <= 320 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalized);
  }

  private trimToNull(value: string): string | null {
    const normalized = value.trim();
    return normalized || null;
  }
}
