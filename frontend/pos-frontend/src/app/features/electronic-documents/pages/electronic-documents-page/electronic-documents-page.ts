import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
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
  ElectronicIssuingJobState,
  issuingStateLabel,
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
export class ElectronicDocumentsPage implements OnInit, OnDestroy {
  private listGeneration = 0;
  private detailGeneration = 0;
  private destroyed = false;
  private contextIdentity(): string {
    return `${this.authStore.companyId()}:${this.authStore.establishmentId()}:${this.authStore.emissionPointId()}:${this.authStore.token()}`;
  }
  ngOnDestroy(): void {
    this.destroyed = true;
    this.listGeneration++;
    this.detailGeneration++;
  }
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
  readonly actionError = signal('');
  readonly selectedDocument = signal<ElectronicDocumentDetail | null>(null);
  readonly actionLoadingKey = signal('');
  readonly recoveryCode = signal('');
  recoveryReference = '';
  recoveryReason = '';
  recoveryAttested = false;

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
    const generation = ++this.listGeneration;
    const context = this.contextIdentity();
    this.loading.set(true);
    this.errorMessage.set('');

    this.documentService.getDocuments(this.currentQuery(page, pageSize)).subscribe({
      next: (result) => {
        if (this.destroyed || generation !== this.listGeneration || context !== this.contextIdentity()) return;
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
        if (this.destroyed || generation !== this.listGeneration || context !== this.contextIdentity()) return;
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
    if (this.actionLoadingKey()) return;
    const generation = ++this.detailGeneration;
    const context = this.contextIdentity();
    this.actionError.set('');
    this.recoveryCode.set('');
    this.clearRecoveryForm();
    this.detailVisible.set(true);
    this.detailLoading.set(true);
    this.detailError.set('');
    this.selectedDocument.set(null);

    this.documentService.getDetail(row.kind, row.id).subscribe({
      next: (detail) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.selectedDocument.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
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
    this.detailGeneration++;
    this.selectedDocument.set(null);
    this.detailError.set('');
  }

  prepareSriDraft(document: ElectronicDocumentDetail): void {
    this.runMutation(document, 'prepare', () =>
      this.documentService.prepareSriDraft(document.kind, document.id)
    );
  }

  signSri(document: ElectronicDocumentDetail): void {
    if (!this.canSign(document)) return;
    this.runMutation(document, 'sign', () =>
      this.documentService.signSri(document.kind, document.id)
    );
  }

  submitSri(document: ElectronicDocumentDetail): void {
    if (!this.canSubmit(document)) return;
    this.runMutation(document, 'submit', () =>
      this.documentService.submitSri(document.kind, document.id)
    );
  }

  checkAuthorization(document: ElectronicDocumentDetail): void {
    if (!this.canCheck(document)) return;
    this.runMutation(document, 'check', () =>
      this.documentService.checkSriAuthorization(document.kind, document.id)
    );
  }

  canOpenRecovery(document: ElectronicDocumentDetail): boolean {
    return document.kind === ElectronicDocumentKind.Invoice && this.canSubmitDocuments()
      && !this.isAuthorized(document) && document.hasSriSignedXml && !!document.accessKey
      && (!!document.issuingJob?.receptionAdmitted || !!document.sriReceptionStatus || !!document.sriSubmittedAt);
  }

  openRecovery(document: ElectronicDocumentDetail): void {
    if (!this.canOpenRecovery(document) || this.actionLoadingKey() || this.selectedDocument()?.key !== document.key) return;
    const generation = this.detailGeneration;
    const context = this.contextIdentity();
    this.clearRecoveryForm();
    this.actionError.set('');
    this.actionLoadingKey.set(`${document.key}:recovery-status`);
    this.documentService.getReceptionRecovery(document.id).subscribe({
      next: (status) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.actionLoadingKey.set('');
        this.recoveryCode.set(status.code);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.actionLoadingKey.set('');
        this.recoveryCode.set('');
        this.actionError.set(this.resolveError(error, 'No se pudo obtener el estado de recuperación.'));
      },
    });
  }

  canConfirmRecovery(document: ElectronicDocumentDetail): boolean {
    return this.canOpenRecovery(document) && this.selectedDocument()?.key === document.key
      && !this.actionLoadingKey() && !this.hasLiveLease(document) && this.recoveryCode() === 'FISCAL_RECOVERY_UNCERTAIN'
      && this.recoveryAttested && (this.recoveryReason.match(/[\p{L}\p{N}]/gu)?.length ?? 0) >= 3;
  }

  confirmRecovery(document: ElectronicDocumentDetail): void {
    if (!this.canConfirmRecovery(document)) return;
    const payload = {
      reference: this.recoveryReference.trim() || null,
      reason: this.recoveryReason.trim(),
      confirmsResendDespiteUncertainReception: this.recoveryAttested,
    };
    this.clearRecoveryForm();
    this.recoveryCode.set('FISCAL_RECOVERY_VERIFY_AFTER_REQUEST');
    this.runMutation(document, 'recover', () => this.documentService.recoverReception(document.id, payload),
      (result) => this.recoveryCode.set(result.code));
  }

  recoveryMessage(code: string): string {
    switch (code) {
      case 'FISCAL_RECOVERY_UNCERTAIN':
        return 'La recepción del XML firmado no está confirmada. Una consulta sin registros no demuestra que el SRI no lo haya recibido.';
      case 'FISCAL_RECOVERY_RECEIVED':
        return 'Existe evidencia de recepción. Consulte la autorización por la clave original; no reenvíe.';
      case 'FISCAL_RECOVERY_PROCESSING':
        return 'El SRI reportó procesamiento. Espere y consulte por la clave original; no reenvíe.';
      case 'FISCAL_RECOVERY_RECONCILE_KEY':
        return 'El SRI reportó la clave registrada (43). Consulte y concilie por la clave original; no reenvíe.';
      case 'FISCAL_RECOVERY_SEQUENCE_CONFLICT':
        return 'El SRI reportó un conflicto de secuencial (45). Investigue el caso con el SRI; no asuma que corresponde a esta factura.';
      case 'FISCAL_RECOVERY_OUTCOME_UNKNOWN':
        return 'El resultado del reenvío es incierto y el único intento ya se consumió. Consulte por la clave original o escale el caso.';
      case 'FISCAL_RECOVERY_CONSUMED':
        return 'El único intento extraordinario ya se utilizó. Consulte el historial de intentos y no reenvíe.';
      case 'FISCAL_RECOVERY_AUTHORIZED':
        return 'El SRI ya había autorizado el comprobante original. Se concilió sin reenviar.';
      case 'SRI_AUTHORIZATION_REJECTED':
      case 'SRI_RECEPTION_REJECTED':
        return 'El SRI rechazó el comprobante. Revise el historial y el motivo fiscal antes de escalar el caso.';
      case 'FISCAL_RECOVERY_CONSULT_INCONCLUSIVE':
        return 'La consulta no confirmó el estado fiscal ni permitió reenviar. Consulte de nuevo o escale el caso.';
      case 'FISCAL_RECOVERY_VALIDITY_INCONCLUSIVE':
        return 'La consulta de validez no confirmó la ausencia de registros. No se realizó ningún reenvío.';
      case 'FISCAL_RECOVERY_VALIDITY_RECORD':
        return 'El SRI registra la clave en la consulta de validez. No reenvíe. Concíliela por la clave original o escale el caso.';
      case 'FISCAL_RECOVERY_AUTHORIZATION_UNVERIFIED':
        return 'El SRI devolvió un registro, pero no se pudo verificar que corresponda al XML original. No reenvíe; concilie o escale el caso.';
      case 'FISCAL_RECOVERY_VERIFY_AFTER_REQUEST':
        return 'Actualice el estado de recuperación para confirmar si el intento se consumió antes de tomar otra acción.';
      default:
        return 'No se permite un reenvío extraordinario. Revise el historial fiscal o escale el caso.';
    }
  }

  private clearRecoveryForm(): void {
    this.recoveryReference = '';
    this.recoveryReason = '';
    this.recoveryAttested = false;
  }

  private hasLiveLease(document: ElectronicDocumentDetail): boolean {
    const expiresAt = document.issuingJob?.leaseExpiresAt;
    return !!expiresAt && Date.parse(expiresAt) > Date.now();
  }

  resumeIssuing(document: ElectronicDocumentDetail): void {
    if (!this.canResume(document)) return;
    this.runMutation(document, 'resume', () => this.documentService.resumeIssuing(document.id));
  }

  issuingLabel(document: ElectronicDocumentListItem): string | null {
    return issuingStateLabel(document.issuingJob?.state);
  }

  issuingSeverity(document: ElectronicDocumentListItem): DocumentTagSeverity {
    const state = document.issuingJob?.state;
    return state === ElectronicIssuingJobState.Authorized ? 'success'
      : state === ElectronicIssuingJobState.Rejected ? 'danger'
      : state === ElectronicIssuingJobState.ManualAttention || state === ElectronicIssuingJobState.TransientFailure ? 'warn' : 'info';
  }

  jobBusy(document: ElectronicDocumentListItem): boolean {
    return document.issuingJob?.state === ElectronicIssuingJobState.Processing;
  }

  canResume(document: ElectronicDocumentDetail): boolean {
    return document.kind === ElectronicDocumentKind.Invoice && this.canSubmitDocuments()
      && this.recoveryCode() !== 'FISCAL_RECOVERY_SEQUENCE_CONFLICT'
      && document.issuingJob?.safeError !== 'FISCAL_RECOVERY_SEQUENCE_CONFLICT'
      && document.issuingJob?.state === ElectronicIssuingJobState.ManualAttention
      && document.documentStatus !== SaleDocumentStatus.Authorized && document.documentStatus !== SaleDocumentStatus.Rejected;
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
      && !this.jobBusy(document)
      && document.documentStatus === SaleDocumentStatus.Draft
      && document.hasSriXmlDraft
      && !document.hasSriSignedXml;
  }

  canSubmit(document: ElectronicDocumentDetail): boolean {
    return this.canSubmitDocuments()
      && !this.jobBusy(document) && !document.issuingJob?.receptionAdmitted
      && document.documentStatus === SaleDocumentStatus.Draft
      && document.hasSriSignedXml
      && !document.sriSubmittedAt
      && !this.normalizeSriStatus(document.sriReceptionStatus);
  }

  canCheck(document: ElectronicDocumentDetail): boolean {
    if (this.canOpenRecovery(document) && this.selectedDocument()?.key === document.key && this.recoveryCode()) {
      return !this.hasLiveLease(document) && ![
        'FISCAL_RECOVERY_SEQUENCE_CONFLICT', 'FISCAL_RECOVERY_NOT_ELIGIBLE',
        'SRI_RECEPTION_REJECTED', 'SRI_AUTHORIZATION_REJECTED',
      ].includes(this.recoveryCode());
    }
    return this.canSubmitDocuments()
      && !this.jobBusy(document)
      && document.issuingJob?.safeError !== 'FISCAL_RECOVERY_SEQUENCE_CONFLICT'
      && (document.issuingJob?.receptionAdmitted === true
        ? !this.isAuthorized(document) && document.documentStatus !== SaleDocumentStatus.Rejected
        : document.documentStatus === SaleDocumentStatus.PendingAuthorization
      && !!document.accessKey
      && !!document.sriSubmittedAt
      && this.normalizeSriStatus(document.sriReceptionStatus) === 'RECIBIDA'
      && this.normalizeSriStatus(document.sriAuthorizationStatus) !== 'AUTORIZADO');
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

  private runMutation<T>(
    document: ElectronicDocumentDetail,
    action: string,
    operation: () => Observable<T>,
    onResult?: (result: T) => void,
  ): void {
    if (this.actionLoadingKey()) {
      return;
    }
    const generation = this.detailGeneration;
    const context = this.contextIdentity();

    this.actionLoadingKey.set(`${document.key}:${action}`);
    this.actionError.set('');
    this.successMessage.set('');
    operation().subscribe({
      next: (result) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.actionLoadingKey.set('');
        const pending = typeof result === 'object' && result !== null && 'pending' in result && result.pending === true;
        if (onResult) onResult(result);
        else this.successMessage.set(action === 'resume' ? 'Trabajo en cola.'
          : pending ? 'Esperando autorización.' : 'Operación SRI completada.');
        this.reloadSelectedDocument(document);
        this.loadDocuments(this.currentPage(), this.rows);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.actionLoadingKey.set('');
        this.actionError.set(this.resolveError(error, 'No se pudo completar la operación SRI.'));
        this.reloadSelectedDocument(document);
      },
    });
  }

  private reloadSelectedDocument(document: ElectronicDocumentDetail): void {
    const generation = this.detailGeneration;
    const context = this.contextIdentity();
    this.detailLoading.set(true);
    this.documentService.getDetail(document.kind, document.id).subscribe({
      next: (detail) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
        this.selectedDocument.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || generation !== this.detailGeneration || context !== this.contextIdentity()) return;
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
