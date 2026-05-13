import { CommonModule, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TabsModule } from 'primeng/tabs';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { PERMISSIONS } from '../../../../core/constants/permissions';
import { PermissionService } from '../../../../core/services/permission.service';
import { resolveHttpErrorMessage } from '../../../../core/utils/http-error-normalizer';
import {
  CompanyFiscalSettings,
  CompanySriSettings,
  DocumentSequence,
  DocumentSequenceAudit,
  FiscalDocumentType,
  SelectOption,
  fiscalDocumentTypeLabel,
  formatFiscalSequential,
} from '../../models/fiscal-settings.model';
import { FiscalSettingsService } from '../../services/fiscal-settings.service';

type SequenceDialogMode = 'create' | 'update';

@Component({
  selector: 'app-fiscal-settings-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DatePipe,
    ButtonModule,
    ConfirmDialogModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    MessageModule,
    SelectModule,
    TableModule,
    TabsModule,
    TagModule,
    TextareaModule,
    ToastModule,
    ToggleSwitchModule,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './fiscal-settings-page.html',
  styleUrl: './fiscal-settings-page.scss',
})
export class FiscalSettingsPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly fiscalSettingsService = inject(FiscalSettingsService);
  private readonly permissionService = inject(PermissionService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  readonly canRead = computed(() => this.permissionService.hasPermission(PERMISSIONS.fiscalSettingsRead));
  readonly canWrite = computed(() => this.permissionService.hasPermission(PERMISSIONS.fiscalSettingsWrite));

  readonly companyLoading = signal(false);
  readonly companySaving = signal(false);
  readonly companyError = signal('');
  readonly companySettings = signal<CompanyFiscalSettings | null>(null);

  readonly sriLoading = signal(false);
  readonly sriSaving = signal(false);
  readonly sriError = signal('');
  readonly sriSettings = signal<CompanySriSettings | null>(null);

  readonly sequences = signal<DocumentSequence[]>([]);
  readonly sequencesLoading = signal(false);
  readonly sequencesError = signal('');
  readonly sequenceSaving = signal(false);
  readonly selectedSequence = signal<DocumentSequence | null>(null);
  readonly sequenceDialogMode = signal<SequenceDialogMode>('update');

  readonly auditSequence = signal<DocumentSequence | null>(null);
  readonly audits = signal<DocumentSequenceAudit[]>([]);
  readonly auditsLoading = signal(false);
  readonly auditsError = signal('');

  sequenceDialogVisible = false;
  auditDialogVisible = false;

  readonly environmentOptions: SelectOption<number>[] = [
    { label: 'Pruebas', value: 1 },
    { label: 'Producción', value: 2 },
  ];

  readonly emissionTypeOptions: SelectOption<number>[] = [{ label: 'Normal', value: 1 }];

  readonly documentTypeOptions: SelectOption<FiscalDocumentType>[] = [
    { label: 'Ticket', value: FiscalDocumentType.Ticket },
    { label: 'Factura', value: FiscalDocumentType.Invoice },
  ];

  readonly companyForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    tradeName: ['', [Validators.maxLength(150)]],
    ruc: ['', [Validators.required, Validators.pattern(/^\d{13}$/)]],
    matrixAddress: ['', [Validators.maxLength(250)]],
    email: ['', [Validators.email, Validators.maxLength(150)]],
    phone: ['', [Validators.maxLength(30)]],
    isAccountingRequired: [false],
    specialTaxpayerNumber: ['', [Validators.maxLength(50)]],
    taxpayerRegime: ['', [Validators.maxLength(80)]],
  });

  readonly sriForm = this.fb.nonNullable.group({
    environment: [1, [Validators.required]],
    emissionType: [1, [Validators.required]],
    isEnabled: [false],
  });

  readonly sequenceForm = this.fb.nonNullable.group({
    establishmentId: [0, [Validators.required, Validators.min(1)]],
    emissionPointId: [0, [Validators.required, Validators.min(1)]],
    documentType: [FiscalDocumentType.Invoice, [Validators.required]],
    nextNumber: [1, [Validators.required, Validators.min(1)]],
    reason: ['', [Validators.required, Validators.maxLength(500)]],
  });

  ngOnInit(): void {
    this.syncWriteAccess();

    if (this.canRead()) {
      this.refreshAll();
    }
  }

  refreshAll(): void {
    this.loadCompanySettings();
    this.loadSriSettings();
    this.loadSequences();
  }

  loadCompanySettings(): void {
    this.companyLoading.set(true);
    this.companyError.set('');

    this.fiscalSettingsService.getCompanySettings().subscribe({
      next: (settings) => {
        this.companySettings.set(settings);
        this.patchCompanyForm(settings);
        this.syncWriteAccess();
        this.companyLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.companyLoading.set(false);
        this.companyError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la configuración fiscal de la empresa.'));
      },
    });
  }

  saveCompanySettings(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.companyForm.invalid) {
      this.companyForm.markAllAsTouched();
      return;
    }

    const values = this.companyForm.getRawValue();
    this.companySaving.set(true);

    this.fiscalSettingsService
      .updateCompanySettings({
        name: values.name.trim(),
        tradeName: this.normalizeOptional(values.tradeName),
        ruc: values.ruc.trim(),
        matrixAddress: this.normalizeOptional(values.matrixAddress),
        email: this.normalizeOptional(values.email),
        phone: this.normalizeOptional(values.phone),
        isAccountingRequired: values.isAccountingRequired,
        specialTaxpayerNumber: this.normalizeOptional(values.specialTaxpayerNumber),
        taxpayerRegime: this.normalizeOptional(values.taxpayerRegime),
      })
      .subscribe({
        next: (settings) => {
          this.companySettings.set(settings);
          this.patchCompanyForm(settings);
          this.companySaving.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Configuración actualizada',
            detail: 'Los datos fiscales de la empresa fueron guardados.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.companySaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  loadSriSettings(): void {
    this.sriLoading.set(true);
    this.sriError.set('');

    this.fiscalSettingsService.getSriSettings().subscribe({
      next: (settings) => {
        this.sriSettings.set(settings);
        this.sriForm.patchValue({
          environment: settings.environment,
          emissionType: settings.emissionType,
          isEnabled: settings.isEnabled,
        });
        this.syncWriteAccess();
        this.sriLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.sriLoading.set(false);
        this.sriError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la configuración SRI.'));
      },
    });
  }

  saveSriSettings(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.sriForm.invalid) {
      this.sriForm.markAllAsTouched();
      return;
    }

    const values = this.sriForm.getRawValue();
    this.sriSaving.set(true);

    this.fiscalSettingsService
      .updateSriSettings({
        environment: values.environment,
        emissionType: values.emissionType,
        isEnabled: values.isEnabled,
      })
      .subscribe({
        next: (settings) => {
          this.sriSettings.set(settings);
          this.sriSaving.set(false);
          this.messageService.add({
            severity: 'success',
            summary: 'Configuración SRI guardada',
            detail: 'El ambiente y tipo de emisión fueron actualizados.',
          });
        },
        error: (error: HttpErrorResponse) => {
          this.sriSaving.set(false);
          this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
        },
      });
  }

  loadSequences(): void {
    this.sequencesLoading.set(true);
    this.sequencesError.set('');

    this.fiscalSettingsService.getDocumentSequences().subscribe({
      next: (sequences) => {
        this.sequences.set(sequences);
        this.sequencesLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.sequencesLoading.set(false);
        this.sequencesError.set(resolveHttpErrorMessage(error, 'No se pudieron cargar los secuenciales.'));
      },
    });
  }

  openCreateSequenceDialog(): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSequence.set(null);
    this.sequenceDialogMode.set('create');
    this.sequenceForm.reset({
      establishmentId: 0,
      emissionPointId: 0,
      documentType: FiscalDocumentType.Invoice,
      nextNumber: 1,
      reason: '',
    });
    this.setSequenceContextControlsDisabled(false);
    this.sequenceDialogVisible = true;
  }

  openUpdateSequenceDialog(sequence: DocumentSequence): void {
    if (!this.canWrite()) {
      return;
    }

    this.selectedSequence.set(sequence);
    this.sequenceDialogMode.set('update');
    this.sequenceForm.reset({
      establishmentId: sequence.establishmentId,
      emissionPointId: sequence.emissionPointId,
      documentType: sequence.documentType,
      nextNumber: sequence.nextNumber,
      reason: '',
    });
    this.setSequenceContextControlsDisabled(true);
    this.sequenceDialogVisible = true;
  }

  onSequenceDialogVisibleChange(visible: boolean): void {
    this.sequenceDialogVisible = visible;

    if (!visible) {
      this.selectedSequence.set(null);
      this.sequenceSaving.set(false);
    }
  }

  confirmSaveSequence(): void {
    if (!this.canWrite()) {
      return;
    }

    if (this.sequenceForm.invalid) {
      this.sequenceForm.markAllAsTouched();
      return;
    }

    this.confirmationService.confirm({
      header: 'Confirmar ajuste de secuencial',
      message: 'Esta acción cambiará el próximo número de documento. El cambio quedará auditado. ¿Deseas continuar?',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Confirmar',
      rejectLabel: 'Cancelar',
      acceptButtonProps: { severity: 'warn' },
      accept: () => this.saveSequence(),
    });
  }

  openAuditDialog(sequence: DocumentSequence): void {
    this.auditSequence.set(sequence);
    this.auditDialogVisible = true;
    this.audits.set([]);
    this.auditsError.set('');
    this.auditsLoading.set(true);

    this.fiscalSettingsService.getDocumentSequenceAudits(sequence.id).subscribe({
      next: (audits) => {
        this.audits.set(audits);
        this.auditsLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.auditsLoading.set(false);
        this.auditsError.set(resolveHttpErrorMessage(error, 'No se pudo cargar la auditoría del secuencial.'));
      },
    });
  }

  onAuditDialogVisibleChange(visible: boolean): void {
    this.auditDialogVisible = visible;

    if (!visible) {
      this.auditSequence.set(null);
      this.audits.set([]);
      this.auditsError.set('');
    }
  }

  formatSequential(value: number | null | undefined): string {
    return formatFiscalSequential(value);
  }

  documentTypeLabel(value: FiscalDocumentType): string {
    return fiscalDocumentTypeLabel(value);
  }

  environmentLabel(value: number | null | undefined): string {
    return value === 2 ? 'Producción' : 'Pruebas';
  }

  emissionTypeLabel(value: number | null | undefined): string {
    return value === 1 ? 'Normal' : 'No válido';
  }

  isProductionEnvironment(): boolean {
    return this.sriForm.controls.environment.value === 2;
  }

  private saveSequence(): void {
    const values = this.sequenceForm.getRawValue();
    const reason = values.reason.trim();
    this.sequenceSaving.set(true);

    if (this.sequenceDialogMode() === 'create') {
      this.fiscalSettingsService
        .createDocumentSequence({
          establishmentId: values.establishmentId,
          emissionPointId: values.emissionPointId,
          documentType: values.documentType,
          nextNumber: values.nextNumber,
          reason,
        })
        .subscribe({
          next: () => this.handleSequenceSaved('Secuencia inicializada.'),
          error: (error: HttpErrorResponse) => this.handleSequenceSaveError(error),
        });
      return;
    }

    const sequence = this.selectedSequence();

    if (!sequence) {
      this.sequenceSaving.set(false);
      return;
    }

    this.fiscalSettingsService
      .updateDocumentSequence(sequence.id, {
        nextNumber: values.nextNumber,
        reason,
      })
      .subscribe({
        next: () => this.handleSequenceSaved('Secuencia ajustada.'),
        error: (error: HttpErrorResponse) => this.handleSequenceSaveError(error),
      });
  }

  private handleSequenceSaved(detail: string): void {
    this.sequenceSaving.set(false);
    this.sequenceDialogVisible = false;
    this.selectedSequence.set(null);
    this.loadSequences();
    this.messageService.add({ severity: 'success', summary: 'Secuencial actualizado', detail });
  }

  private handleSequenceSaveError(error: HttpErrorResponse): void {
    this.sequenceSaving.set(false);
    this.messageService.add({ severity: 'error', summary: 'Error', detail: resolveHttpErrorMessage(error) });
  }

  private patchCompanyForm(settings: CompanyFiscalSettings): void {
    this.companyForm.patchValue({
      name: settings.name,
      tradeName: settings.tradeName ?? '',
      ruc: settings.ruc,
      matrixAddress: settings.matrixAddress ?? '',
      email: settings.email ?? '',
      phone: settings.phone ?? '',
      isAccountingRequired: settings.isAccountingRequired,
      specialTaxpayerNumber: settings.specialTaxpayerNumber ?? '',
      taxpayerRegime: settings.taxpayerRegime ?? '',
    });
  }

  private syncWriteAccess(): void {
    if (this.canWrite()) {
      this.companyForm.enable({ emitEvent: false });
      this.sriForm.enable({ emitEvent: false });
      return;
    }

    this.companyForm.disable({ emitEvent: false });
    this.sriForm.disable({ emitEvent: false });
    this.sequenceForm.disable({ emitEvent: false });
  }

  private setSequenceContextControlsDisabled(disabled: boolean): void {
    const controls = [
      this.sequenceForm.controls.establishmentId,
      this.sequenceForm.controls.emissionPointId,
      this.sequenceForm.controls.documentType,
    ];

    for (const control of controls) {
      if (disabled) {
        control.disable({ emitEvent: false });
      } else {
        control.enable({ emitEvent: false });
      }
    }

    this.sequenceForm.controls.nextNumber.enable({ emitEvent: false });
    this.sequenceForm.controls.reason.enable({ emitEvent: false });
  }

  private normalizeOptional(value: string): string | null {
    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
  }
}
